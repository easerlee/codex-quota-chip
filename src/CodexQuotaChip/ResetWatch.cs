namespace CodexQuotaChip;

// Did a quota window just roll over? When a window resets, the server moves its reset time
// forward, so "the reset time is later than the one already on file" is the signal. That needs no
// clock of our own, cannot fire twice for the same rollover however often the app restarts, and
// does not care whether the app was running while it happened.
//
// A window that had no reset time on file is the *start* of a window (nothing has been used since
// the last reset), not a rollover, so it stays quiet.
public static class ResetWatch
{
    public static ResetRollover Check(long? beforeFiveHour, long? beforeWeek, long? fiveHour, long? week) =>
        new(Rolled(beforeFiveHour, fiveHour), Rolled(beforeWeek, week));

    private static bool Rolled(long? before, long? now) =>
        before is long was && now is long isNow && isNow > was;
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
