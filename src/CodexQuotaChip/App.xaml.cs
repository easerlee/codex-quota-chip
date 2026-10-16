using System.Text.Json;
using System.Windows.Threading;

namespace CodexQuotaChip;

public partial class App : Application
{
    private const string MutexName = @"Local\CodexQuotaChip";

    private Log? _log;
    private Win32.SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = Paths.Discover(e.Args);
        _log = new Log(paths.LogPath);
        DispatcherUnhandledException += OnUnhandledException;

        var settings = SettingsFile.Load(paths.Settings, _log);
        ApplyOverrides(settings, e.Args);
        var codexHome = Paths.CodexHome(e.Args);
        var codexExe = new CodexExe(Value(e.Args, "--codex-exe"), _log);

        if (Has(e.Args, "--check"))
        {
            Check(paths, codexHome, codexExe);
            Shutdown();
            return;
        }

        if (Has(e.Args, "--once"))
        {
            Once(codexHome, codexExe);
            Shutdown();
            return;
        }

        _instance = new Win32.SingleInstance();
        if (!_instance.TryAcquire(MutexName))
        {
            _log.Info("another instance is already running, exiting");
            Shutdown();
            return;
        }

        _log.Info($"started, state in {paths.Root}, codex home {codexHome}, codex.exe {codexExe.Current ?? "(not found)"}");
        var window = new MainWindow(paths, codexHome, settings, _log, codexExe);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    // A background overlay must never die silently: leave the reason in the log on the way out.
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("unhandled exception", e.Exception);
        e.Handled = false;
    }

    // --check: what the app found and where it would park itself, then exit.
    private void Check(Paths paths, string codexHome, CodexExe codexExe)
    {
        Win32.AttachToParentConsole();
        var placement = CodexQuotaChip.MainWindow.ResolvePlacement(116, false);
        Console.WriteLine($"state   : {paths.Root}");
        Console.WriteLine($"settings: {paths.Settings}");
        Console.WriteLine($"codex   : {codexHome}");
        Console.WriteLine($"exe     : {codexExe.Current ?? "(not found)"}");
        Console.WriteLine($"place   : {placement.X},{placement.Y} {placement.Width}x{placement.Height}");
    }

    // --once: read the quota a single time and print it, without starting the overlay.
    private void Once(string codexHome, CodexExe codexExe)
    {
        Win32.AttachToParentConsole();
        var reading = new QuotaClient(codexHome, _log!, codexExe.Resolve).ReadAsync().GetAwaiter().GetResult();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            five_hour = reading.FiveHour is null ? null : new { left = reading.FiveHour.RemainingPercent, resets_at = reading.FiveHour.ResetsAt },
            week = reading.Weekly is null ? null : new { left = reading.Weekly.RemainingPercent, resets_at = reading.Weekly.ResetsAt },
            plan = reading.PlanType,
            error = reading.Error
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ApplyOverrides(Settings settings, string[] args)
    {
        if (int.TryParse(Value(args, "--refresh-seconds"), out var seconds))
        {
            settings.RefreshSeconds = seconds;
        }

        settings.Normalize();
    }

    private static bool Has(string[] args, string name) =>
        args.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

    private static string? Value(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
