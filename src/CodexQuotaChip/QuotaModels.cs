using System.Text.Json;

namespace CodexQuotaChip;

// One rate-limit window as the server described it: how much is left and when it refills.
public sealed record UsageWindow(int? DurationMinutes, double? RemainingPercent, long? ResetsAt);

// The reading for whoever is logged in right now.
public sealed record QuotaReading(
    UsageWindow? FiveHour,
    UsageWindow? Weekly,
    string? PlanType,
    DateTimeOffset At,
    string? Error = null)
{
    public static QuotaReading Failed(string error) => new(null, null, null, DateTimeOffset.Now, error);

    public bool Ok => Error is null;
}

// Parsing and window classification, kept free of any process handling so it can be tested
// against a recorded answer.
public static class QuotaParser
{
    public static QuotaReading Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Parse(document.RootElement);
        }
        catch (JsonException error)
        {
            return QuotaReading.Failed($"unreadable answer: {error.Message}");
        }
    }

    // account/rateLimits/read answers with { rateLimits: { primary, secondary, planType } }.
    public static QuotaReading Parse(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object ||
            !answer.TryGetProperty("rateLimits", out var limits) ||
            limits.ValueKind != JsonValueKind.Object)
        {
            return QuotaReading.Failed("answer carried no rateLimits");
        }

        var (fiveHour, weekly) = Classify([Window(limits, "primary"), Window(limits, "secondary")]);
        return new QuotaReading(fiveHour, weekly, Text(limits, "planType"), DateTimeOffset.Now);
    }

    // Which window is the 5-hour one is decided by its own duration, never by position: the
    // server has swapped primary/secondary around before. Position is only used as a fallback
    // for older builds that left windowDurationMins out entirely.
    public static (UsageWindow? FiveHour, UsageWindow? Weekly) Classify(IReadOnlyList<UsageWindow?> windows)
    {
        UsageWindow? Match(int minutes, int tolerance) => windows.FirstOrDefault(
            window => window?.DurationMinutes is { } duration && Math.Abs(duration - minutes) <= tolerance);

        var fiveHour = Match(300, 5);
        var weekly = Match(10_080, 60);

        if (fiveHour is null && weekly is null &&
            windows.Count >= 2 && windows[0] is { DurationMinutes: null } && windows[1] is { DurationMinutes: null })
        {
            (fiveHour, weekly) = (windows[0], windows[1]);
        }

        return (fiveHour, weekly);
    }

    private static UsageWindow? Window(JsonElement limits, string name)
    {
        if (!limits.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var used = Number(element, "usedPercent");
        return new UsageWindow(
            Integer(element, "windowDurationMins"),
            used.HasValue ? Math.Clamp(100.0 - used.Value, 0.0, 100.0) : null,
            Long(element, "resetsAt"));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Integer(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;

    private static long? Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : null;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) ? result : null;
}
