using System.IO;
using CodexQuotaChip;

// Self-tests for everything in this app that has a rule worth stating: reading an answer and
// classifying its windows, turning a reading into text, ordering accounts, parking the overlay,
// the click-outside rule, the settings file and re-resolving codex.exe.
// Run with: dotnet run --project tests/CodexQuotaChip.Tests
var tests = new (string Name, Action Body)[]
{
    ("quota answer parsing", TestParsing),
    ("windows are classified by duration, not position", TestClassification),
    ("reading is turned into text", TestReadout),
    ("panel orders accounts by who frees up first", TestAccountOrder),
    ("overlay parks on both display sides", TestPlacement),
    ("bar geometry ignores the label", TestBarGeometry),
    ("click outside closes the panel", TestDismiss),
    ("settings defaults and repair", TestSettings),
    ("codex.exe path is re-resolved when it disappears", TestExeReResolve),
    ("autostart registry entry", TestAutostart),
    ("account identity from auth.json", TestAccountIdentity),
    ("account history file", TestAccountFile)
};

var passed = 0;
var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
        passed++;
    }
    catch (Exception error)
    {
        // Keep going: one failure must not hide the results of every test after it.
        Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}");
        failed++;
    }
}

Console.WriteLine($"{passed}/{tests.Length} tests passed" + (failed > 0 ? $", {failed} failed" : ""));
return failed > 0 ? 1 : 0;

static void TestParsing()
{
    var reading = QuotaParser.Parse("""
        {
          "rateLimits": {
            "limitId": "codex",
            "planType": "plus",
            "primary":   { "usedPercent": 57.25, "windowDurationMins": 300,   "resetsAt": 1800000000 },
            "secondary": { "usedPercent": 16.0,  "windowDurationMins": 10080, "resetsAt": 1800600000 }
          }
        }
        """);

    Equal(null, reading.Error, "a good answer carries no error");
    Equal(42.75, reading.FiveHour?.RemainingPercent, "remaining is 100 minus used");
    Equal(84.0, reading.Weekly?.RemainingPercent, "weekly remaining");
    Equal(1800000000L, reading.FiveHour?.ResetsAt, "5h reset time");
    Equal("plus", reading.PlanType, "plan type");

    var bad = QuotaParser.Parse("""{ "somethingElse": true }""");
    Equal(true, bad.Error is not null, "an answer without rateLimits is an error");
}

static void TestClassification()
{
    // The server has swapped primary/secondary around before, so duration decides.
    var (five, week) = QuotaParser.Classify(
    [
        new UsageWindow(10080, 84, 1),
        new UsageWindow(300, 42, 2)
    ]);
    Equal(42.0, five?.RemainingPercent, "the 300-minute window is the 5h one wherever it sits");
    Equal(84.0, week?.RemainingPercent, "the 10080-minute window is the weekly one");

    // Older builds left the duration out entirely; only then does position decide.
    var (olderFive, olderWeek) = QuotaParser.Classify(
    [
        new UsageWindow(null, 10, 3),
        new UsageWindow(null, 20, 4)
    ]);
    Equal(10.0, olderFive?.RemainingPercent, "without durations, primary is the 5h window");
    Equal(20.0, olderWeek?.RemainingPercent, "without durations, secondary is the weekly window");
}

static void TestReadout()
{
    var now = DateTimeOffset.Now;
    var future = now.AddHours(3).ToUnixTimeSeconds();
    var past = now.AddMinutes(-5).ToUnixTimeSeconds();

    Equal(true, Readout.Spent(100, future, now), "a full window has no trustworthy clock");
    Equal(true, Readout.Spent(0, past, now), "a window past its reset is spent");
    Equal(false, Readout.Spent(42, future, now), "a running window is not spent");

    Equal("42%", Readout.Percent(42.4), "percentage is rounded");
    Equal("--", Readout.Percent(null), "no reading prints dashes");
    Equal("已重置", Readout.When(0, past, now, weekly: false), "a spent window says so");
    Equal("--:--", Readout.When(null, null, now, weekly: false), "a clock that never existed");
    Equal(Readout.Clock(future), Readout.When(42, future, now, weekly: false), "5h shows its clock");
    Equal(Readout.Countdown(future), Readout.When(42, future, now, weekly: true), "the week counts down");

    var epoch = 1_800_000_000L;
    Equal(
        DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime().ToString("HH:mm"),
        Readout.Clock(epoch),
        "clock format");
    Equal("reset now", Readout.Countdown(now.AddMinutes(-1).ToUnixTimeSeconds()), "an expired countdown");
}

static void TestAccountOrder()
{
    var now = DateTimeOffset.Now;
    AccountRecord Record(string id, double? left, long? resetsIn, DateTimeOffset? seen = null) =>
        new(id, id + "@example.com", "plus", seen ?? now.AddMinutes(-5), left,
            resetsIn, 50, now.AddDays(3).ToUnixTimeSeconds());

    var running = Record("running", 40, now.AddHours(4).ToUnixTimeSeconds(), now);
    var soon = Record("soon", 80, now.AddHours(1).ToUnixTimeSeconds(), now.AddMinutes(-30));
    var already = Record("already", 0, now.AddMinutes(-10).ToUnixTimeSeconds(), now.AddMinutes(-60));
    var full = Record("full", 100, now.AddHours(5).ToUnixTimeSeconds(), now.AddMinutes(-90));
    var unknown = Record("unknown", null, null, now.AddMinutes(-120));

    var ordered = AccountOrder.Sort([running, unknown, soon, already, full], "running", now);
    Equal("running", ordered[0].Id, "the logged-in account leads whatever its own reset time is");
    Equal("already", ordered[1].Id, "a window that already reset is usable now");
    Equal("full", ordered[2].Id, "so is a window that never started counting");
    Equal("soon", ordered[3].Id, "then the nearest reset time");
    Equal("unknown", ordered[4].Id, "a record with no reset time falls to the bottom");

    var tieA = Record("tie-a", 30, now.AddHours(2).ToUnixTimeSeconds(), now.AddMinutes(-20));
    var tieB = Record("tie-b", 30, now.AddHours(2).ToUnixTimeSeconds(), now.AddMinutes(-1));
    Equal("tie-b", AccountOrder.Sort([tieA, tieB], null, now)[0].Id, "ties break on the latest reading");
}

static void TestPlacement()
{
    var bottom = new Win32.Rect { Left = 0, Top = 1032, Right = 1920, Bottom = 1080 };

    Equal(
        new WindowPlacement(1232, 1032, 260, 48),
        Placement.Compute(Placement.EdgeBottom, bottom, 260, 48, 1920, 1080, trayLeft: 1500),
        "the tray side sits immediately left of the notification area");

    Equal(
        new WindowPlacement(Placement.Gap, 1032, 260, 48),
        Placement.Compute(Placement.EdgeBottom, bottom, 260, 48, 1920, 1080, trayLeft: 1500, pinLeft: true),
        "the left side sits at the taskbar's own left end");

    Equal(
        new WindowPlacement(0, 1032, 260, 48),
        Placement.Compute(Placement.EdgeBottom, bottom, 260, 48, 1920, 1080, trayLeft: 0),
        "with no tray geometry the tray side falls back to the taskbar's left edge");

    Equal(
        new WindowPlacement(0, 1032, 260, 48),
        Placement.Compute(Placement.EdgeLeft, new Win32.Rect { Left = 0, Top = 0, Right = 48, Bottom = 1080 }, 260, 48, 1920, 1080, 0),
        "a vertical taskbar falls back to the bottom-left corner");

    Equal(
        new WindowPlacement(1660, 1032, 260, 48),
        Placement.Compute(Placement.EdgeBottom, bottom, 260, 48, 1920, 1080, trayLeft: 5000),
        "an off-screen anchor is clamped back onto the monitor");
}

// The two overlay bars must start at the same x, so their range may depend on the row geometry
// only - never on how wide the label happened to measure.
static void TestBarGeometry()
{
    Equal(GaugeRow.BarRange(116, 16), GaugeRow.BarRange(116, 16), "same geometry, same bar");
    var narrow = GaugeRow.BarRange(116, 12);
    var tall = GaugeRow.BarRange(116, 20);
    Equal(true, tall.Left >= narrow.Left, "a taller row never starts its bar further left");
    Equal(114.0, narrow.Right, "the bar ends two pixels inside the row");
}

static void TestDismiss()
{
    Equal(true, Dismiss.ShouldClose(pressed: true, wasUp: true, inside: false), "a fresh press outside closes");
    Equal(false, Dismiss.ShouldClose(pressed: true, wasUp: true, inside: true), "a press inside belongs to the panel");
    Equal(false, Dismiss.ShouldClose(pressed: true, wasUp: false, inside: false), "a press held over from before does not");
    Equal(false, Dismiss.ShouldClose(pressed: false, wasUp: true, inside: false), "a release never closes");
}

static void TestSettings()
{
    var directory = Path.Combine(Path.GetTempPath(), "cqc-settings-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "settings.json");
    var log = new Log(Path.Combine(directory, "test.log"));
    try
    {
        var defaults = SettingsFile.Load(path, log);
        Equal(60, defaults.RefreshSeconds, "default refresh interval");
        Equal(false, defaults.DisplayLeft, "the overlay starts on the tray side");

        File.WriteAllText(path, """{ "refresh_seconds": 900, "display_side": "left", "warn_below": 40, "critical_below": 20 }""");
        var loaded = SettingsFile.Load(path, log);
        Equal(900, loaded.RefreshSeconds, "loaded refresh interval");
        Equal(true, loaded.DisplayLeft, "loaded display side");

        File.WriteAllText(path, """{ "refresh_seconds": 1, "display_side": "sideways" }""");
        var repaired = SettingsFile.Load(path, log);
        Equal(30, repaired.RefreshSeconds, "an absurd interval is raised to the floor");
        Equal("right", repaired.DisplaySide, "an unknown display side falls back to the tray side");

        File.WriteAllText(path, "{ not json at all");
        Equal(60, SettingsFile.Load(path, log).RefreshSeconds, "a corrupt file falls back to defaults");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestExeReResolve()
{
    var directory = Path.Combine(Path.GetTempPath(), "cqc-exe-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var exe = Path.Combine(directory, "codex.exe");
    File.WriteAllText(exe, string.Empty);
    try
    {
        var searches = 0;
        string? Search(string? _)
        {
            searches++;
            return exe;
        }

        // A remembered path that still exists is used as it is.
        Equal(exe, CodexLocator.Fresh(exe, null, Search), "a live path is kept");
        Equal(0, searches, "and no search is run for it");

        // The CLI moves itself on update: the remembered path is gone, so a fresh search runs.
        Equal(exe, CodexLocator.Fresh(Path.Combine(directory, "gone.exe"), null, Search), "a stale path is re-resolved");
        Equal(1, searches, "which runs the search exactly once");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestAutostart()
{
    // A scratch value name: the test must never touch the entry the installed app uses.
    const string name = "CodexQuotaChipSelfTest";
    var directory = Path.Combine(Path.GetTempPath(), "cqc-autostart-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var log = new Log(Path.Combine(directory, "test.log"));
    try
    {
        Autostart.SetEnabled(false, log, name);
        Equal(false, Autostart.IsEnabled(name), "the scratch entry starts absent");

        if (!Autostart.SetEnabled(true, log, name))
        {
            // A confined environment can deny the registry write; that is not a code failure.
            Console.WriteLine("     (skipped: this environment does not allow writing the Run key)");
            return;
        }

        Equal(true, Autostart.IsEnabled(name), "autostart reports enabled");
        Equal(true, Autostart.SetEnabled(false, log, name), "and can be turned off again");
        Equal(false, Autostart.IsEnabled(name), "autostart reports disabled");
    }
    finally
    {
        Autostart.SetEnabled(false, log, name);
        Directory.Delete(directory, recursive: true);
    }
}

static void TestAccountIdentity()
{
    // A JWT with a payload only: the signature is irrelevant to reading the account.
    static string Token(string payload)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
        var part = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"header.{part}.signature";
    }

    var token = Token("""
        { "email": "user@example.com", "https://api.openai.com/auth": { "chatgpt_plan_type": "plus" } }
        """);
    var identity = AccountIdentity.Parse($$"""
        { "tokens": { "account_id": "abc-123", "id_token": "{{token}}" } }
        """);
    Equal("abc-123", identity?.Id, "account id");
    Equal("user@example.com", identity?.Email, "email from the token");
    Equal("plus", identity?.Plan, "plan from the token");
    Equal("user", AccountIdentity.Label(identity?.Email, "abc-123"), "the label is the part before the @");

    Equal(null, AccountIdentity.Parse("""{ "tokens": {} }"""), "an empty auth file means unknown");
    Equal("abcd…wxyz", AccountIdentity.Label(null, "abcdefghijklwxyz"), "no email falls back to a short id");
}

static void TestAccountFile()
{
    var directory = Path.Combine(Path.GetTempPath(), "cqc-accounts-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "accounts.json");
    try
    {
        var store = new AccountFile(path);
        Equal(0, store.Load().Count, "no records to begin with");

        store.Upsert(new AccountRecord("a", "a@example.com", "plus", DateTimeOffset.Now, 42, 1, 84, 2));
        store.Upsert(new AccountRecord("b", "b@example.com", "plus", DateTimeOffset.Now, 10, 3, 50, 4));
        Equal(2, store.Load().Count, "both accounts are remembered");

        store.Upsert(new AccountRecord("a", "a@example.com", "plus", DateTimeOffset.Now, 7, 5, 84, 2));
        var records = store.Load();
        Equal(2, records.Count, "re-reading one account replaces its record instead of adding one");
        Equal(7.0, records.Single(record => record.Id == "a").FiveHourLeft, "and keeps the newer values");

        File.WriteAllText(path, "{ broken");
        Equal(0, store.Load().Count, "a corrupt file reads as empty instead of throwing");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void Equal<T>(T expected, T actual, string what)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{what}: expected {expected}, got {actual}");
    }
}
