namespace CodexQuotaChip;

// The overlay belongs to the taskbar strip, so it steps aside while a fullscreen application
// owns the screen and comes back when that application is gone.
public static class Fullscreen
{
    // Windows that cover the monitor without being a fullscreen application.
    private static readonly string[] ShellClasses =
        ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow"];

    public static bool Covers(string windowClass, Win32.Rect window, Win32.Rect monitor)
    {
        if (ShellClasses.Contains(windowClass, StringComparer.Ordinal))
        {
            return false;
        }

        // Covering the monitor includes covering the strip the taskbar occupies; a maximised
        // window stops above it.
        return window.Left <= monitor.Left &&
            window.Top <= monitor.Top &&
            window.Right >= monitor.Right &&
            window.Bottom >= monitor.Bottom;
    }
}
