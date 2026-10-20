namespace CodexQuotaChip;

// The overlay is drawn straight onto the taskbar, so its colours follow the taskbar's own
// light/dark setting; the right-click panel is always dark and has its own fixed palette.
//
// The overlay can also put the panel's dark plate behind itself. Then the chip is dark whatever
// the taskbar is, which is why every overlay colour is asked for with the plate flag: near-black
// text on a near-black plate is exactly the mistake that keeps from happening.
public static class Theme
{
    public static bool LightTaskbar { get; } = ReadLightTaskbar();

    private static bool Dark(bool plate) => plate || !LightTaskbar;

    public static Color Text(bool plate) => Hex(Dark(plate) ? "#F4F6F8" : "#1B2027");

    public static Color Track(bool plate) => Hex(Dark(plate) ? "#6A56616C" : "#33000000");

    public static Color Ok(bool plate) => Hex(Dark(plate) ? "#2DD4A8" : "#0F8A69");

    public static Color Warn(bool plate) => Hex(Dark(plate) ? "#FFC857" : "#A45A00");

    public static Color Fail(bool plate) => Hex(Dark(plate) ? "#FF6B81" : "#C62A45");

    public static Color Idle(bool plate) => Hex(Dark(plate) ? "#8794A2" : "#5B6672");

    public static Color ForRemaining(double? remainingPercent, Settings settings)
    {
        if (!remainingPercent.HasValue)
        {
            return Idle(settings.Plate);
        }
        if (remainingPercent.Value < settings.CriticalBelow)
        {
            return Fail(settings.Plate);
        }
        return remainingPercent.Value < settings.WarnBelow ? Warn(settings.Plate) : Ok(settings.Plate);
    }

    public static Color Hex(string value) => (Color)ColorConverter.ConvertFromString(value);

    private static bool ReadLightTaskbar()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static class Panel
    {
        // Opaque on purpose: #F0141619 left 6% of whatever is behind it showing through, which is
        // invisible over a dark desktop and reads as a ghost of the chat window over a light one.
        public static Color Background => Hex("#FF141619");

        public static Color Outline => Hex("#33FFFFFF");

        public static Color Text => Hex("#F2F4F7");

        public static Color Dim => Hex("#8B94A1");

        public static Color Hover => Hex("#1FFFFFFF");

        // Dim enough that an empty bar reads as empty rather than as a full grey one.
        public static Color Track => Hex("#1CFFFFFF");

        public static Color Ok => Hex("#2DD4A8");

        public static Color Warn => Hex("#FFC857");

        public static Color Fail => Hex("#FF6B81");

        public static Color Idle => Hex("#8794A2");

        public static Color ForRemaining(double? remainingPercent, Settings settings)
        {
            if (!remainingPercent.HasValue)
            {
                return Idle;
            }
            if (remainingPercent.Value < settings.CriticalBelow)
            {
                return Fail;
            }
            return remainingPercent.Value < settings.WarnBelow ? Warn : Ok;
        }
    }
}
