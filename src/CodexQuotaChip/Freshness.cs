namespace CodexQuotaChip;

// How long a reading stays "live". One rule for the whole app: the overlay calls a reading stale
// after this, and the panel puts the same light in front of each account - green while the app is
// still hearing from that account, grey once it has gone quiet. A revoked token shows up here.
public static class Freshness
{
    // Two periods, or the period plus a few seconds when the period itself is tiny: a read that
    // takes a second or two must not make a healthy account look dead.
    public static TimeSpan Window(int refreshSeconds) =>
        TimeSpan.FromSeconds(Math.Max(refreshSeconds * 2.0, refreshSeconds + 5.0));

    public static bool IsLive(DateTimeOffset lastSeen, DateTimeOffset now, int refreshSeconds) =>
        now - lastSeen <= Window(refreshSeconds);
}
