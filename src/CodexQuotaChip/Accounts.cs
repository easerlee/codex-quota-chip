using System.Text.Json;

namespace CodexQuotaChip;

// The last reading seen for one account while it was the logged-in one.
public sealed record AccountRecord(
    string Id,
    string? Email,
    string? Plan,
    DateTimeOffset LastSeenAt,
    double? FiveHourLeft,
    long? FiveHourResetsAt,
    double? WeekLeft,
    long? WeekResetsAt,
    int? ResetCredits = null,
    long? ResetCreditsExpireAt = null);

// One record per account, rewritten in place: because the key is the account, switching away
// leaves the old record exactly as it was, which is the point - it shows what that account had
// when it was last used.
public sealed class AccountFile
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;

    public AccountFile(string path)
    {
        _path = path;
    }

    public IReadOnlyList<AccountRecord> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<AccountRecord>>(File.ReadAllText(_path), Json) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public void Upsert(AccountRecord record)
    {
        try
        {
            var records = Load()
                .Where(existing => !string.Equals(existing.Id, record.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            records.Add(record);

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(records, Json));
            File.Move(temp, _path, true);
        }
        catch (Exception)
        {
            // Remembering accounts is a convenience; it must never break a refresh.
        }
    }
}

// Panel order: the logged-in account leads, then whoever becomes usable soonest. The 5-hour
// window is always the earlier of the two, so its reset time decides; a window that already
// reset, or has not started counting, is usable now and sorts above one still running. Records
// with no reset time at all fall to the bottom.
public static class AccountOrder
{
    public static IReadOnlyList<AccountRecord> Sort(IReadOnlyList<AccountRecord> records, string? currentId, DateTimeOffset now) =>
        records
            .OrderByDescending(record => IsCurrent(record, currentId))
            .ThenBy(record => UsableAt(record, now))
            .ThenByDescending(record => record.LastSeenAt)
            .ToList();

    public static bool IsCurrent(AccountRecord record, string? currentId) =>
        currentId is not null && string.Equals(record.Id, currentId, StringComparison.OrdinalIgnoreCase);

    private static long UsableAt(AccountRecord record, DateTimeOffset now)
    {
        if (record.FiveHourResetsAt is not { } resetsAt)
        {
            return long.MaxValue;
        }

        return Readout.Spent(record.FiveHourLeft, resetsAt, now) ? 0 : resetsAt;
    }
}

// Which account the CLI is logged in as, read from its own auth file. Strictly read-only: this
// app never writes, refreshes or logs credentials.
public sealed record AccountIdentity(string Id, string? Email, string? Plan)
{
    public static string Label(string? email, string id)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var at = email.IndexOf('@');
            return at > 0 ? email[..at] : email;
        }

        return id.Length <= 10 ? id : $"{id[..4]}…{id[^4..]}";
    }

    public static AccountIdentity? Read(string codexHome)
    {
        try
        {
            var path = Path.Combine(codexHome, "auth.json");
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception)
        {
            // Missing, locked or half-written: simply "unknown account".
            return null;
        }
    }

    public static AccountIdentity? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var id = Text(tokens, "account_id");
            var claims = Claims(Text(tokens, "id_token"));
            var email = claims.HasValue ? Text(claims.Value, "email") : null;
            string? plan = null;
            if (claims.HasValue &&
                claims.Value.TryGetProperty("https://api.openai.com/auth", out var auth) &&
                auth.ValueKind == JsonValueKind.Object)
            {
                plan = Text(auth, "chatgpt_plan_type");
            }

            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            return new AccountIdentity(string.IsNullOrWhiteSpace(id) ? email! : id!, email, plan);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The id token is a JWT; only its payload is of interest and its signature is irrelevant.
    private static JsonElement? Claims(string? token)
    {
        var parts = token?.Split('.');
        if (parts is null || parts.Length < 2)
        {
            return null;
        }

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        while (payload.Length % 4 != 0)
        {
            payload += "=";
        }

        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.Clone();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
