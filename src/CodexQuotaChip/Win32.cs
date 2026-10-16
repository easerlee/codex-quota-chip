using System.Runtime.InteropServices;
using System.Text;

namespace CodexQuotaChip;

// The handful of Win32 calls the overlay needs to behave like part of the taskbar rather than
// like a window that happens to sit on top of it.
public static class Win32
{
    public const int GwlExStyle = -20;
    public const int GwlpHwndParent = -8;
    public const int GaRoot = 2;

    public const int WsExToolWindow = 0x00000080;
    public const int WsExAppWindow = 0x00040000;
    public const int WsExNoActivate = 0x08000000;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpFrameChanged = 0x0020;
    public const uint SwpShowWindow = 0x0040;
    public const uint SwpNoOwnerZOrder = 0x0200;

    public const int VkLeftButton = 0x01;
    public const int VkRightButton = 0x02;
    public const int ErrorAlreadyExists = 183;

    public static readonly IntPtr HwndTopmost = new(-1);

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public bool IsEmpty => Width <= 0 || Height <= 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, StringBuilder buffer, int count);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    private const uint MonitorDefaultToPrimary = 1;
    private const uint MonitorDefaultToNearest = 2;

    // Full monitor bounds in physical pixels: the monitor the window is on, or the primary one.
    public static Rect MonitorBounds(IntPtr hwnd)
    {
        var monitor = hwnd != IntPtr.Zero ? MonitorFromWindow(hwnd, MonitorDefaultToNearest) : IntPtr.Zero;
        if (monitor == IntPtr.Zero)
        {
            monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfoW(monitor, ref info))
        {
            return info.Monitor;
        }

        return new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    }

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateMutexW(IntPtr attributes, bool initialOwner, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    public static extern uint GetLastError();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    public const uint AttachParentProcess = 0xFFFFFFFF;

    public static bool AttachToParentConsole() => AttachConsole(AttachParentProcess);

    public static IntPtr RootWindow(IntPtr hwnd)
    {
        var root = GetAncestor(hwnd, GaRoot);
        return root == IntPtr.Zero ? hwnd : root;
    }

    public static IntPtr TaskbarWindow() => FindWindowW("Shell_TrayWnd", null);

    // Left edge of the notification area (tray icons plus clock) on the primary taskbar.
    public static int TrayLeftEdge()
    {
        var taskbar = TaskbarWindow();
        if (taskbar == IntPtr.Zero)
        {
            return 0;
        }

        var tray = FindWindowExW(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        return tray != IntPtr.Zero && GetWindowRect(tray, out var rect) && !rect.IsEmpty ? rect.Left : 0;
    }

    public static bool TryTaskbarRect(out Rect rect)
    {
        var taskbar = TaskbarWindow();
        if (taskbar != IntPtr.Zero && GetWindowRect(taskbar, out rect) && !rect.IsEmpty)
        {
            return true;
        }

        rect = default;
        return false;
    }

    public static bool TryWindowRect(IntPtr hwnd, out Rect rect) => GetWindowRect(hwnd, out rect);

    public static bool TryCursor(out Point point) => GetCursorPos(out point);

    public static bool MouseButtonDown() =>
        (GetAsyncKeyState(VkLeftButton) & 0x8000) != 0 || (GetAsyncKeyState(VkRightButton) & 0x8000) != 0;

    public static string WindowClass(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassNameW(hwnd, buffer, buffer.Capacity);
        return length <= 0 ? string.Empty : buffer.ToString(0, length);
    }

    // The overlay is a tool window that never activates: it must not take focus from whatever
    // the user is typing into, and it must not appear in the alt-tab list.
    public static void MakeOverlayWindow(IntPtr hwnd)
    {
        hwnd = RootWindow(hwnd);
        var style = (int)GetWindowLongPtrValue(hwnd, GwlExStyle);
        var updated = (style | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow;
        if (updated == style)
        {
            return;
        }

        SetWindowLongPtrValue(hwnd, GwlExStyle, updated);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    // Owning the overlay to the taskbar keeps it composited directly above the shell: without
    // it the shell raises itself over the overlay for a frame whenever the taskbar is clicked.
    public static void Own(TaskbarOwner owner)
    {
        var hwnd = RootWindow(owner.Handle);
        var taskbar = TaskbarWindow();
        if (taskbar == IntPtr.Zero || GetWindow(hwnd, 4) == taskbar)
        {
            return;
        }

        SetWindowLongPtrValue(hwnd, GwlpHwndParent, (int)taskbar);
    }

    public static void RaiseWithoutActivating(IntPtr hwnd)
    {
        hwnd = RootWindow(hwnd);
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder);
    }

    public static void PlaceWithoutActivating(IntPtr hwnd, WindowPlacement placement)
    {
        hwnd = RootWindow(hwnd);
        SetWindowPos(hwnd, HwndTopmost, placement.X, placement.Y, placement.Width, placement.Height,
            SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder);
    }

    private static IntPtr GetWindowLongPtrValue(IntPtr hwnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : GetWindowLong32(hwnd, index);

    private static void SetWindowLongPtrValue(IntPtr hwnd, int index, int value)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(hwnd, index, new IntPtr(value));
        }
        else
        {
            SetWindowLong32(hwnd, index, value);
        }
    }

    // Keeps only one overlay alive; the second instance exits immediately.
    public sealed class SingleInstance : IDisposable
    {
        private IntPtr _handle;

        public bool TryAcquire(string name)
        {
            _handle = CreateMutexW(IntPtr.Zero, false, name);
            if (_handle != IntPtr.Zero && GetLastError() != ErrorAlreadyExists)
            {
                return true;
            }

            Dispose();
            return false;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}

// Marker so the ownership call reads as what it does at the call site.
public readonly record struct TaskbarOwner(IntPtr Handle);
