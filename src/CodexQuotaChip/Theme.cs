namespace CodexQuotaChip;

// The overlay is drawn straight onto the taskbar, so its colours follow the taskbar's own
// light/dark setting; the right-click panel is always dark and has its own fixed palette.
public static class Theme
{
    public static bool LightTaskbar { get; } = ReadLightTaskbar();

    public static Color Text => Hex(LightTaskbar ? "#1B2027" : "#F4F6F8");

    public static Color Track => Hex(LightTaskbar ? "#33000000" : "#6A56616C");

    public static Color Ok => Hex(LightTaskbar ? "#0F8A69" : "#2DD4A8");

    public static Color Warn => Hex(LightTaskbar ? "#A45A00" : "#FFC857");

    public static Color Fail => Hex(LightTaskbar ? "#C62A45" : "#FF6B81");

    public static Color Idle => Hex(LightTaskbar ? "#5B6672" : "#8794A2");

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
        public static Color Background => Hex("#F0141619");

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
