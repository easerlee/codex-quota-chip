using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexQuotaChip;

// Asks the Codex CLI's own local app-server for the current account's rate limits. The Codex
// side is only ever read: one process is started, asked, and killed.
public sealed class QuotaClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly string _codexHome;
    private readonly Log _log;
    private readonly Func<string?> _resolveExe;

    public QuotaClient(string codexHome, Log log, Func<string?> resolveExe)
    {
        _codexHome = codexHome;
        _log = log;
        _resolveExe = resolveExe;
    }

    public async Task<QuotaReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        var codexExe = _resolveExe();
        if (string.IsNullOrWhiteSpace(codexExe))
        {
            return QuotaReading.Failed("codex.exe not found");
        }

        try
        {
            using var server = AppServer.Start(codexExe, _codexHome);
            await server.InitializeAsync(Timeout, cancellationToken).ConfigureAwait(false);
            var answer = await server.CallAsync("account/rateLimits/read", Timeout, cancellationToken).ConfigureAwait(false);
            return QuotaParser.Parse(answer);
        }
        catch (Exception error)
        {
            _log.Warn($"quota read failed: {error.Message}");
            return QuotaReading.Failed(Readout.Compact(error.Message));
        }
    }
}

// One app-server process, spoken to over stdio with newline-delimited JSON-RPC 2.0. Server
// notifications (anything without our id) are ignored; only the answers matter here.
internal sealed class AppServer : IDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stderr;
    private int _nextId = 1;

    private AppServer(Process process)
    {
        _process = process;
        _stderr = process.StandardError.ReadToEndAsync();
    }

    public static AppServer Start(string codexExe, string codexHome)
    {
        var startInfo = new ProcessStartInfo(codexExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");
        startInfo.Environment["CODEX_HOME"] = codexHome;

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("could not start codex app-server");
        return new AppServer(process);
    }

    // The version the app actually is, not a string that was true once: this goes out to codex on
    // every start, and a stale one makes its logs lie about who is asking.
    private static string ClientVersion =>
        typeof(QuotaClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task InitializeAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await CallAsync(
            "initialize",
            new
            {
                clientInfo = new { name = "codex-quota-chip", version = ClientVersion },
                capabilities = new { experimentalApi = true }
            },
            timeout,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<JsonElement> CallAsync(string method, TimeSpan timeout, CancellationToken cancellationToken) =>
        CallAsync(method, null, timeout, cancellationToken);

    private async Task<JsonElement> CallAsync(string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var id = _nextId++;
        var request = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        });

        var input = _process.StandardInput;
        await input.WriteLineAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
        await input.FlushAsync(cancellationToken).ConfigureAwait(false);

        // One pending read at a time, kept across timeouts: re-issuing it would leave two
        // readers on the same stream.
        // One pending read at a time, and only ever issued after the previous one has been
        // consumed: starting a second read on the same stream fails with "the stream is
        // currently in use". A read that is still pending when the answer arrives must not be
        // left behind either - the next call on this process would collide with it.
        Task<string?>? pending = null;
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException($"{method} timed out{await StderrHintAsync().ConfigureAwait(false)}");
            }

            pending ??= _process.StandardOutput.ReadLineAsync();
            if (await Task.WhenAny(pending, Task.Delay(remaining, cancellationToken)).ConfigureAwait(false) != pending)
            {
                continue;
            }

            var line = await pending.ConfigureAwait(false);
            pending = null;

            if (line is null)
            {
                if (_process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"app-server exited with code {_process.ExitCode}{await StderrHintAsync().ConfigureAwait(false)}");
                }
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue; // app-server prints the odd non-JSON line; it is not an answer.
            }

            using (document)
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var answerId) ||
                    !answerId.TryGetInt32(out var answered) || answered != id)
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    throw new InvalidOperationException(error.GetRawText());
                }

                if (!root.TryGetProperty("result", out var result))
                {
                    throw new InvalidOperationException("answer carried no result");
                }

                return result.Clone();
            }
        }
    }

    // app-server reports startup problems on stderr only, so put them in the error message.
    private async Task<string> StderrHintAsync()
    {
        if (await Task.WhenAny(_stderr, Task.Delay(400)).ConfigureAwait(false) != _stderr)
        {
            return string.Empty;
        }

        var text = (await _stderr.ConfigureAwait(false)).Trim();
        // Capped so one runaway line cannot fill the log, but long enough to keep the reason.
        return text.Length == 0 ? string.Empty : $": {Readout.Compact(text, 2000)}";
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }
        catch (Exception)
        {
            // Best effort: the process dies with us anyway.
        }
        finally
        {
            _process.Dispose();
        }
    }
}
