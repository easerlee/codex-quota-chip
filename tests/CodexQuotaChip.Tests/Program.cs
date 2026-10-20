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
    ("a reset is nearly a whole window, not drift", TestResetWatch),
    ("reset credits come out of the same answer", TestResetCredits),
    ("a refilled window shows a full bar", TestBarPercent),
    ("the light follows the link, not the login", TestFreshness),
    ("the strip is measured from its longest line", TestStripWidth),
    ("a window that ran out of time is reported once", TestRefillWatch),
    ("the panel's background is opaque", TestPanelIsOpaque),
    ("the notification payload is well formed", TestToastPayload),
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

static void TestResetWatch()
{
    const long was = 1_700_000_000;
    const long fiveHourLater = was + 5 * 3600;          // 5 小时窗口整轮翻篇
    const long weekLater = was + 7 * 24 * 3600;         // 周窗口整轮翻篇
    const long drifted = was + 300;                     // 服务端把周窗口末尾往后滑了几分钟

    // A rollover moves the end of the window forward by nearly the whole window.
    var five = ResetWatch.Check(was, was, fiveHourLater, was);
    Equal(true, five.FiveHour, "a five-hour rollover counts");
    Equal(false, five.Week, "the week window did not move");
    Equal(true, five.Any, "so something rolled over");

    var week = ResetWatch.Check(was, was, was, weekLater);
    Equal(false, week.FiveHour, "an unchanged five-hour window");
    Equal(true, week.Week, "a weekly rollover counts");
    Equal(true, ResetWatch.Check(was, was, fiveHourLater, weekLater).Any, "both at once counts");

    // The bug this rule exists to prevent: the weekly window's end slides along with usage, so a
    // few minutes - or an hour - of drift between readings is not a reset. "Moved forward at all"
    // announced a reset every couple of minutes.
    Equal(false, ResetWatch.Check(was, was, drifted, drifted).Any, "a few minutes of drift is not a reset");
    Equal(false, ResetWatch.Check(was, was, was, was + 3600).Any, "an hour of drift on the week window is not a reset");
    Equal(false, ResetWatch.Check(was, was, was, was + 18_000).Any, "five hours is still not most of a week");

    // No news: the same time, an older one, or nothing on file - a window with no reset time is a
    // window starting, not one ending.
    Equal(false, ResetWatch.Check(was, was, was, was).Any, "an unchanged reset time is not a reset");
    Equal(false, ResetWatch.Check(fiveHourLater, fiveHourLater, was, was).Any, "an earlier reset time is not a reset");
    Equal(false, ResetWatch.Check(null, null, fiveHourLater, weekLater).Any, "a window that had no reset time is not a reset");
    Equal(false, ResetWatch.Check(was, was, null, null).Any, "a window that lost its reset time is not a reset");
    Equal(false, ResetWatch.Check(was, null, was, weekLater).Week,
        "a window the file knew nothing about is a start, not a reset");

    // The wording has to say which window and what is available now.
    Equal("5 小时额度已重置，现在剩余 100%",
        ResetWatch.Check(was, was, fiveHourLater, was).Describe(100, 42), "5-hour wording");
    Equal("周额度已重置，现在剩余 8%",
        ResetWatch.Check(was, was, was, weekLater).Describe(50, 8.0), "week wording");
    Equal("5 小时和周额度都已重置，额度已恢复",
        ResetWatch.Check(was, was, fiveHourLater, weekLater).Describe(null, null), "both windows, no figures");
}

static void TestResetCredits()
{
    // The shape recorded from a live account/rateLimits/read: the reset section sits beside
    // rateLimits, and the list carries credits that are no longer usable.
    var reading = QuotaParser.Parse("""
        {
          "rateLimits": {
            "primary":   { "usedPercent": 58, "windowDurationMins": 300,   "resetsAt": 1792275253 },
            "secondary": { "usedPercent": 71, "windowDurationMins": 10080, "resetsAt": 1792768211 },
            "planType": "plus"
          },
          "rateLimitResetCredits": {
            "availableCount": 2,
            "credits": [
              { "status": "redeemed",  "expiresAt": 1790000000 },
              { "status": "available", "expiresAt": 1795000000 },
              { "status": "available", "expiresAt": 1794009383 },
              { "status": "expired",   "expiresAt": 1780000000 }
            ]
          }
        }
        """);

    Equal(true, reading.Ok, "the answer is still a good reading");
    Equal(true, reading.Resets is not null, "the reset section is picked up");
    Equal(2, reading.Resets!.Available, "the server's own available count is used as-is");
    Equal(true, (long?)1794009383L == reading.Resets.ExpiresAt, "the soonest available expiry wins, redeemed and expired ones do not count");

    // No section: an account or build without the feature, which is not an error.
    var bare = QuotaParser.Parse("""{ "rateLimits": { "primary": { "usedPercent": 0, "windowDurationMins": 300 } } }""");
    Equal(true, bare.Resets is null, "no section, no tickets");
    Equal(true, bare.Ok, "and that is not an error either");

    // The wording and the expiry rule behind the icon.
    var expiry = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds();
    var one = new ResetCredits(1, expiry);
    Equal("1 次额度重置可用（最近 2026-11-06 到期）", one.Describe(DateTimeOffset.FromUnixTimeSeconds(expiry).AddDays(-1)), "one ticket, with the date it expires");
    Equal("3 次额度重置可用", new ResetCredits(3, null).Describe(DateTimeOffset.Now), "several tickets, no expiry to mention");
    Equal(false, one.Expired(DateTimeOffset.FromUnixTimeSeconds(expiry).AddSeconds(-1)), "alive a second before");
    Equal(true, one.Expired(DateTimeOffset.FromUnixTimeSeconds(expiry)), "gone at the expiry second");
    Equal(false, new ResetCredits(1, null).Expired(DateTimeOffset.Now), "an expiry that is not known cannot have passed");
}

static void TestToastPayload()
{
    var payload = Notifier.BuildXml("Codex 额度已重置", "5 小时额度已重置，现在剩余 100%");
    Equal(true, payload.StartsWith("<toast>"), "starts with the toast element");
    Equal(true, payload.EndsWith("</toast>"), "closes it");
    Equal(true, payload.Contains("<text>Codex 额度已重置</text>"), "the title is in it");
    Equal(true, payload.Contains("<text>5 小时额度已重置，现在剩余 100%</text>"), "so is the body");
    Equal(true, payload.Contains("template=\"ToastGeneric\""), "and the template the shell expects");

    // The one thing here with a wrong answer: text that the XML reader would otherwise eat.
    var escaped = Notifier.BuildXml("a & b", "5 < 6 > 4 & 2");
    Equal(true, escaped.Contains("<text>a &amp; b</text>"), "an ampersand in the title");
    Equal(true, escaped.Contains("<text>5 &lt; 6 &gt; 4 &amp; 2</text>"), "and comparisons in the body");
    Equal(10, escaped.Count(character => character == '<'), "only the markup's own brackets are left");
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
    Equal(108.0, narrow.Right, "the bar ends as far inside the row as the label starts");
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
        Equal(true, defaults.Plate, "the dark plate is on by default");

        File.WriteAllText(path, """{ "refresh_seconds": 900, "display_side": "left", "warn_below": 40, "critical_below": 20 }""");
        var loaded = SettingsFile.Load(path, log);
        Equal(900, loaded.RefreshSeconds, "loaded refresh interval");
        Equal(true, loaded.DisplayLeft, "loaded display side");
        Equal(true, loaded.Plate, "a settings file from before the plate existed keeps it on");

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

static void TestBarPercent()
{
    var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    // The reset time has passed: the window refilled, so the bar is full even though the stored
    // reading is whatever it was before the reset.
    Equal(100.0, ChipPanel.BarPercent(3.0, 1_799_999_999, now), "a passed reset time reads as a full bar");

    // Being full is the other way a window is spent: never used is not the same as empty.
    Equal(100.0, ChipPanel.BarPercent(100.0, 1_800_000_600, now), "a full window reads as a full bar");

    // Everything else is the reading itself, and no reading at all is an empty bar.
    Equal(42.0, ChipPanel.BarPercent(42.0, 1_800_000_600, now), "a live window keeps its reading");
    Equal(0.0, ChipPanel.BarPercent(null, null, now), "no reading is an empty bar");
}

static void TestFreshness()
{
    var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    Equal(120.0, Freshness.Window(60).TotalSeconds, "a one-minute refresh is quiet after two periods");
    Equal(1800.0, Freshness.Window(900).TotalSeconds, "a slow one gets two periods");
    Equal(7.0, Freshness.Window(2).TotalSeconds, "a tiny period still gets a few seconds of slack");

    Equal(true, Freshness.IsLive(now.AddSeconds(-10), now, 60), "a reading from a moment ago is live");
    Equal(true, Freshness.IsLive(now.AddSeconds(-120), now, 60), "the window itself still counts as live");
    Equal(false, Freshness.IsLive(now.AddSeconds(-121), now, 60), "a second past it is not live");

    Equal(true, Freshness.IsLive(now.AddMinutes(-20), now, 900), "a slow refresh stays live longer");
    Equal(false, Freshness.IsLive(now.AddMinutes(-31), now, 900), "and gives up after its own window");
}

static void TestStripWidth()
{
    // The line that used to be cut off ("11:25 / 6d..."), measured the way the bottom row draws it.
    var real = ResetRow.LineWidth("11:25 / 6d 20h", 16, 1.0);
    var needed = ResetRow.RequiredWidth(16, 1.0);
    Equal(true, needed >= real, "the bottom row fits a real countdown");

    // 116 was the fixed width that did not fit it; the strip is measured now, and the margins are
    // on top of what the row itself asks for.
    var strip = MainWindow.StripWidth(1.0);
    Equal(true, strip > 116, "the strip is wider than the fixed width it used to have");
    Equal(true, strip >= needed + 6, "and it covers the row plus the strip's own margins");
}

static void TestRefillWatch()
{
    var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    // A window that had been used, whose reset time has gone by, is full again - the refill that used
    // to go unannounced because that account is not the one being read.
    Equal(true, ResetWatch.Refilled(0, 1_799_999_999, now), "a used window that ran out of time has refilled");

    // Nothing to say about a window that was already full, one still running, or one never read.
    Equal(false, ResetWatch.Refilled(100, 1_799_999_999, now), "a window that was already full says nothing");
    Equal(false, ResetWatch.Refilled(0, 1_800_000_001, now), "a window with time left says nothing");
    Equal(false, ResetWatch.Refilled(null, 1_799_999_999, now), "no reading, no claim");
    Equal(false, ResetWatch.Refilled(42, null, now), "no reset time, no claim");

    // What the app writes back when it reports one - full, with the reset time dropped - is what
    // keeps the same window from being reported again.
    Equal(false, ResetWatch.Refilled(100, null, now), "a reported refill is not reported again");
}

static void TestPanelIsOpaque()
{
    // It used to be #F0141619 - 6% transparent, which let the desktop read through the panel and
    // looked like a rendering fault over a light window.
    Equal((byte)255, Theme.Panel.Background.A, "the panel's own background is opaque");
}

static void Equal<T>(T expected, T actual, string what)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{what}: expected {expected}, got {actual}");
    }
}
