using Microsoft.Win32;

namespace CodexQuotaChip;

// The autostart entry is a plain HKCU Run value pointing at this executable.
public static class Autostart
{
    public const string ValueName = "CodexQuotaChip";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled(string valueName = ValueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(valueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled, Log log, string valueName = ValueName)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                log.Warn("autostart unchanged: Run key unavailable");
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
                return true;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                log.Warn("autostart unchanged: executable path unknown");
                return false;
            }

            key.SetValue(valueName, $"\"{exe}\"", RegistryValueKind.String);
            return true;
        }
        catch (Exception error)
        {
            log.Warn($"autostart unchanged: {error.Message}");
            return false;
        }
    }
}
