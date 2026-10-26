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

    // 多屏时这条规则要拿「浮窗所在的那块屏」去比，不是窗口自己那块屏 —— 副屏上最大化的窗口盖的是
    // 副屏，主屏任务栏上的浮窗并没有被挡住。测试里用两块屏的矩形把这件事钉住。
    public static bool ParksChip(string windowClass, Win32.Rect window, Win32.Rect chipMonitor) =>
        Covers(windowClass, window, chipMonitor);
}
