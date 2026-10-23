using System.Globalization;

namespace CodexQuotaChip;

// How a reading is turned into the short strings the overlay and the panel show. All of it is
// pure so the rules can be tested without a window.
public static class Readout
{
    // A window has no trustworthy clock when it is full (the server only starts the 5-hour
    // window when it is first used) or when its reset time has already passed.
    public static bool Spent(double? remainingPercent, long? resetsAt, DateTimeOffset now) =>
        (remainingPercent.HasValue && remainingPercent.Value >= 99.5) ||
        (resetsAt.HasValue && resetsAt.Value <= now.ToUnixTimeSeconds());

    public static string Percent(double? remainingPercent) =>
        remainingPercent.HasValue ? $"{Math.Round(remainingPercent.Value):0}%" : "--";

    // What the panel prints next to a percentage: the clock for 5H, the countdown for the week.
    public static string When(double? remainingPercent, long? resetsAt, DateTimeOffset now, bool weekly)
    {
        if (Spent(remainingPercent, resetsAt, now))
        {
            return Strings.ResetLabel;
        }

        if (!resetsAt.HasValue)
        {
            return weekly ? "reset --" : "--:--";
        }

        return weekly ? Countdown(resetsAt) : Clock(resetsAt);
    }

    public static string Clock(long? epochSeconds) =>
        epochSeconds.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value).ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)
            : "--:--";

    public static string Countdown(long? epochSeconds)
    {
        if (!epochSeconds.HasValue)
        {
            return "reset --";
        }

        var span = TimeSpan.FromSeconds(epochSeconds.Value - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (span.TotalSeconds <= 0)
        {
            return "reset now";
        }
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays}d {span.Hours}h";
        }
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }
        return $"{Math.Max(0, span.Minutes)}m";
    }

    // Row text size derived from the row height, so a taller or shorter taskbar stays balanced.
    public static double TextSize(double rowHeight) => Math.Clamp(rowHeight * 0.78, 8.5, 12.0);

    public static string Compact(string message, int limit = 180)
    {
        var text = message.Trim();
        return text.Length <= limit ? text : text[..(limit - 3)] + "...";
    }
}
