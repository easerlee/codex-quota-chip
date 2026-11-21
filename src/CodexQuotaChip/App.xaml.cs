using System.Text.Json;
using System.Threading;
using System.Windows.Interop;
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

        // Before any window exists: the app id the process answers to has to be the one its
        // notifications are sent under.
        Win32.SetCurrentProcessExplicitAppUserModelID(Notifier.Aumid);

        var paths = Paths.Discover(e.Args);
        _log = new Log(paths.LogPath);
        DispatcherUnhandledException += OnUnhandledException;

        var settings = SettingsFile.Load(paths.Settings, _log);
        ApplyOverrides(settings, e.Args);
        // 语言在任何界面出现之前定下来：--notify-test 那条路也会用到文案。
        Strings.Use(settings.Language);
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

        // --work-check: 只读地跑一遍「Codex 还活着吗」的判定，把结论和每份文件的证据打出来。
        // 用来核对判定，不关机、不改任何设置。
        if (Has(e.Args, "--work-check"))
        {
            WorkCheck(codexHome);
            Shutdown();
            return;
        }

        if (Has(e.Args, "--notify-test"))
        {
            NotifyTest(_log);
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
        // The width the overlay works out for itself, 1.0 pixels per dip because there is no window
        // here to ask and the taskbar sits on the primary monitor. The height comes from the bar
        // itself, so a small or two-row taskbar reports the strip it will really get.
        var height = CodexQuotaChip.MainWindow.TaskbarThickness();
        var width = CodexQuotaChip.MainWindow.StripWidth(height, 1.0);
        var placement = CodexQuotaChip.MainWindow.ResolvePlacement(width, false, waitForAnchors: true);
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

    private static void WorkCheck(string codexHome)
    {
        Win32.AttachToParentConsole();
        var now = DateTimeOffset.Now;
        var reading = CodexWork.Inspect(codexHome, now);
        Console.WriteLine($"busy    : {(reading.Busy ? "yes" : "no")}");
        Console.WriteLine($"tag     : {reading.Tag}");
        Console.WriteLine($"evidence: {reading.Evidence}");
        Console.WriteLine($"last    : {(reading.LastActivity is { } at ? at.ToLocalTime().ToString("u") : "(none)")}");
        Console.WriteLine($"beats   : {string.Join(", ", CodexWork.HeartbeatThreads(codexHome))}");

        // 每份文件的证据行：判定错了，能一眼看出是哪个会话在顶。
        foreach (var file in CodexWork.RecentSessions(codexHome))
        {
            var state = CodexWork.Read(file.FullName);
            var age = (now.UtcDateTime - file.LastWriteTimeUtc).TotalSeconds;
            var (busy, tag) = CodexWork.Classify(state, age);
            Console.WriteLine(
                $"  {(busy ? "BUSY" : "    ")} {tag,-10} age={age,8:0}s open={(state.Open ? 1 : 0)} write={(state.Write ? 1 : 0)} hb={(state.Heartbeat ? 1 : 0)} quota={(state.Quota ? 1 : 0)} {file.Name}");
        }
    }

    // --notify-test: prove the notification works without waiting for a real reset. It needs a
    // window handle, so a hidden window is shown just long enough to hold the balloon up.
    private static void NotifyTest(Log log)
    {
        Win32.AttachToParentConsole();
        var window = new Window
        {
            Width = 1,
            Height = 1,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Opacity = 0
        };
        window.Show();

        using var notifier = new Notifier(new WindowInteropHelper(window).Handle, log);
        var shown = notifier.Show(Strings.ResetToastTitle, Strings.NotifyTestBody);

        Console.WriteLine(shown
            ? "notification sent (look above the notification area)"
            : "the shell refused the notification; see the log for the reason");
        Thread.Sleep(TimeSpan.FromSeconds(8));
        window.Close();
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
