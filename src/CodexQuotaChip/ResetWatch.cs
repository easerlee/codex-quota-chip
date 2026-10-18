namespace CodexQuotaChip;

// Did a quota window just roll over? When a window resets, the server moves its end forward by
// nearly the whole window - about five hours for the 5-hour one, about a week for the weekly one -
// so that is the test.
//
// It cannot be "the reset time moved forward at all": the server slides the weekly window's end
// along with usage, so a few minutes of drift between two readings looked exactly like a reset and
// the app announced one every couple of minutes. Requiring most of a window to have passed leaves
// room for any amount of drift, and still catches a window that rolled over while the app was shut.
//
// A window with no reset time on file is the *start* of a window (nothing has been used since the
// last reset), not a rollover, so it stays quiet.
public static class ResetWatch
{
    private const int FiveHourMinutes = 300;
    private const int WeekMinutes = 10_080;

    // How much of the window has to have moved for this to be a rollover rather than drift.
    private const double RolloverFraction = 0.7;

    public static ResetRollover Check(long? beforeFiveHour, long? beforeWeek, long? fiveHour, long? week) =>
        new(Rolled(beforeFiveHour, fiveHour, FiveHourMinutes), Rolled(beforeWeek, week, WeekMinutes));

    // The other half of the question, for windows that are not being read. An account that is not
    // logged in is not being used, so nothing is consuming its quota: when the reset time it last
    // reported goes by, the window is full again. That is the same reading the panel already shows
    // as 已重置 - this is what makes the app say it out loud.
    //
    // A window that was already full when it ran out of time has not changed, and one that never had
    // a reading cannot be claimed either way, so both stay quiet.
    public static bool Refilled(double? left, long? resetsAt, DateTimeOffset now) =>
        left is < 99.5 && resetsAt is long end && end <= now.ToUnixTimeSeconds();

    private static bool Rolled(long? before, long? now, int windowMinutes) =>
        before is long was &&
        now is long isNow &&
        isNow > was &&
        isNow - was >= RolloverFraction * windowMinutes * 60;
}

public readonly record struct ResetRollover(bool FiveHour, bool Week)
{
    public bool Any => FiveHour || Week;

    // What to say about it: one line per window that rolled over, shortest useful wording.
    public string Describe(double? fiveHourLeft, double? weekLeft)
    {
        string Left(double? value) => value is null ? "额度已恢复" : $"现在剩余 {(int)Math.Round(value.Value)}%";

        if (FiveHour && Week)
        {
            return $"5 小时和周额度都已重置，{Left(fiveHourLeft)}";
        }

        return FiveHour
            ? $"5 小时额度已重置，{Left(fiveHourLeft)}"
            : $"周额度已重置，{Left(weekLeft)}";
    }
}
