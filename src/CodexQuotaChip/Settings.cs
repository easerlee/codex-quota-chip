using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexQuotaChip;

// Everything the app remembers between runs, in a file a human can edit.
public sealed class Settings
{
    public const int DefaultRefreshSeconds = 60;

    [JsonPropertyName("refresh_seconds")]
    public int RefreshSeconds { get; set; } = DefaultRefreshSeconds;

    // Which end of the taskbar the overlay sits on: "right" = immediately left of the
    // notification area, "left" = the taskbar's own far end (empty on a centred Windows 11
    // taskbar).
    [JsonPropertyName("display_side")]
    public string DisplaySide { get; set; } = "right";

    // Remaining percentage below which the reading turns amber, and below that, red.
    [JsonPropertyName("warn_below")]
    public double WarnBelow { get; set; } = 30;

    [JsonPropertyName("critical_below")]
    public double CriticalBelow { get; set; } = 15;

    [JsonIgnore]
    public bool DisplayLeft => string.Equals(DisplaySide, "left", StringComparison.OrdinalIgnoreCase);

    public Settings Copy() => new()
    {
        RefreshSeconds = RefreshSeconds,
        DisplaySide = DisplaySide,
        WarnBelow = WarnBelow,
        CriticalBelow = CriticalBelow
    };

    public void Normalize()
    {
        RefreshSeconds = Math.Max(30, RefreshSeconds);
        WarnBelow = Math.Clamp(WarnBelow, 0, 100);
        CriticalBelow = Math.Clamp(CriticalBelow, 0, WarnBelow);
        DisplaySide = DisplayLeft ? "left" : "right";
    }
}

public static class SettingsFile
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load(string path, Log log)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new Settings();
            }

            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings();
            settings.Normalize();
            return settings;
        }
        catch (Exception error)
        {
            log.Warn($"settings unreadable, using defaults: {error.Message}");
            return new Settings();
        }
    }

    public static void Save(string path, Settings settings, Log log)
    {
        try
        {
            settings.Normalize();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json) + Environment.NewLine);
            File.Move(temp, path, true);
        }
        catch (Exception error)
        {
            log.Warn($"settings not saved: {error.Message}");
        }
    }
}
