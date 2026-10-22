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

    // GetWindow: the window directly behind the one asked about.
    public const uint GwHwndNext = 2;

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

    // Name the process the same way it names its notifications. An unpackaged program has no app id
    // of its own, and a toast whose sender can be named is one the shell is happy to keep.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

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

    // Both ends of the taskbar as the shell lays them out: where the right-hand cluster starts (the
    // tray, or the widget pill Windows 11 draws in front of it) and where the left-hand one ends
    // (the app buttons, or 0 on a centred taskbar whose far left is empty). Neither the pill nor that
    // edge is a window, so UIAutomation is asked - cached, and re-asked when the tray moves or the
    // widget switch changes.
    public readonly record struct TaskbarAnchors(int RightLeft, int LeftRight);

    public static TaskbarAnchors Anchors(bool wait = false)
    {
        var taskbar = TaskbarWindow();
        if (taskbar == IntPtr.Zero)
        {
            return default;
        }

        var tray = FindWindowExW(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        var trayLeft = tray != IntPtr.Zero && GetWindowRect(tray, out var trayRect) && !trayRect.IsEmpty
            ? trayRect.Left
            : 0;

        if (wait)
        {
            var widgets = WidgetsEnabled();
            var task = Task.Run(() => QueryAnchors(taskbar, trayLeft, widgets));
            task.Wait(2000);
            if (task.IsCompletedSuccessfully)
            {
                _anchors = task.Result;
                _anchorTrayLeft = trayLeft;
                _anchorWidgets = widgets;
                _anchorAt = Environment.TickCount64;
            }

            return _anchors;
        }

        return Cached(taskbar, trayLeft);
    }

    // The widget switch is the one thing that changes this layout without moving a window, so it is
    // read on every placement - it costs 0.14 ms, against 20 ms for the UIAutomation query.
    private static int WidgetsEnabled()
    {
        using var advanced = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        return advanced?.GetValue("TaskbarDa") as int? ?? 1;
    }

    private const int AnchorCacheMs = 5_000;
    private static TaskbarAnchors _anchors;
    private static int _anchorTrayLeft;
    private static int? _anchorWidgets;
    private static long _anchorAt;
    private static int _anchorBusy;
    private static int _pillLeft;

    // UIAutomation wants an MTA apartment, and the placement timer runs on WPF's STA thread where the
    // query comes back empty. So the question is asked on a thread of its own and the answer is
    // cached; this call uses whatever the last answer was.
    private static TaskbarAnchors Cached(IntPtr taskbar, int trayLeft)
    {
        var widgets = WidgetsEnabled();
        if (trayLeft == _anchorTrayLeft
            && widgets == _anchorWidgets
            && Environment.TickCount64 - _anchorAt < AnchorCacheMs)
        {
            return _anchors;
        }

        if (Interlocked.Exchange(ref _anchorBusy, 1) == 0)
        {
            _anchorTrayLeft = trayLeft;
            _anchorWidgets = widgets;
            _ = Task.Run(() =>
            {
                try
                {
                    _anchors = QueryAnchors(taskbar, trayLeft, widgets);
                }
                catch (Exception)
                {
                    // No automation, no answer: the tray's own edge is the old behaviour and works.
                }
                finally
                {
                    _anchorAt = Environment.TickCount64;
                    Interlocked.Exchange(ref _anchorBusy, 0);
                }
            });
        }

        return _anchors;
    }

    private static TaskbarAnchors QueryAnchors(IntPtr taskbar, int trayLeft, int widgets)
    {
        var bar = System.Windows.Automation.AutomationElement.FromHandle(taskbar);
        var found = bar?.FindAll(
            System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.ControlTypeProperty,
                System.Windows.Automation.ControlType.Button));

        if (found is null)
        {
            return new TaskbarAnchors(_pillLeft > 0 ? _pillLeft : trayLeft, 0);
        }

        var buttons = new List<(int Left, int Right, bool TaskList)>();
        foreach (System.Windows.Automation.AutomationElement button in found)
        {
            var rect = button.Current.BoundingRectangle;
            var taskList = button.Current.ClassName?.StartsWith("Taskbar.TaskListButton", StringComparison.Ordinal) == true;
            buttons.Add(((int)rect.Left, (int)rect.Right, taskList));
        }

        _pillLeft = Placement.KeepPill(widgets, Placement.PillLeftEdge(trayLeft, buttons), _pillLeft);
        return new TaskbarAnchors(
            _pillLeft > 0 ? _pillLeft : trayLeft,
            Placement.TaskListRightEdge(buttons));
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

    // 层级只有一条规则：任务栏 < 浮窗 < 菜单，各差一层。
    //
    // 浮窗用「插到任务栏正上方」，位置关系是确定的：比任务栏低就被它盖住，比它高就看得见，
    // 谁刷新都不会把顺序换过来（不用「提到最顶」，那会连系统菜单一起压）。
    public static void RaiseToTop(IntPtr hwnd)
    {
        SetWindowPos(RootWindow(hwnd), HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    public static void RideAbove(IntPtr hwnd, IntPtr below)
    {
        hwnd = RootWindow(hwnd);
        below = RootWindow(below);
        if (hwnd == IntPtr.Zero || below == IntPtr.Zero || GetWindow(hwnd, GwHwndNext) == below)
        {
            return;
        }

        // No SWP_SHOWWINDOW here: the card is hidden on purpose while a fullscreen app owns the
        // screen, and a z-order move must never be what brings it back.
        SetWindowPos(hwnd, below, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    // The card sits one layer above the bar. Anything else the shell puts above the bar (its own
    // menus) lands underneath the card by this rule - which is why a menu over the strip makes the
    // card step aside instead of arguing about z-order.
    public static void RideAboveTaskbar(IntPtr hwnd)
    {
        var taskbar = TaskbarWindow();
        if (taskbar != IntPtr.Zero)
        {
            RideAbove(hwnd, taskbar);
        }
    }

    // The shell's own menus and flyouts are windows of their own, and they are what the card must
    // never cover: Win11 draws them as Xaml_WindowedPopupClass, a classic menu is #32768, and the
    // overflow island is named after itself. Their z-order cannot be relied on - a card raised to
    // the top of the topmost band ends up *above* a menu that was inserted just above the bar, which
    // is exactly the "额度条压在任务管理器上" screenshot. So the card steps aside instead: while one
    // of them is over the strip it is hidden, and it comes back when the menu closes.
    public static bool ShellMenuOver(IntPtr hwnd, Rect strip)
    {
        var found = false;
        EnumWindows((window, _) =>
        {
            if (found || window == hwnd || !IsWindowVisible(window)
                || !GetWindowRect(window, out var box) || box.IsEmpty)
            {
                return !found;
            }

            var name = WindowClass(window);
            if (name != "#32768"
                && !name.StartsWith("Xaml_WindowedPopupClass", StringComparison.Ordinal)
                && !name.StartsWith("TopLevelWindowForOverflowXamlIsland", StringComparison.Ordinal))
            {
                return true;
            }

            if (box.Left >= strip.Right || box.Right <= strip.Left
                || box.Top >= strip.Bottom || box.Bottom <= strip.Top)
            {
                return true;
            }

            found = true;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    // A placement only moves and resizes. It must not touch the z-order: HWND_TOPMOST without
    // SWP_NOZORDER also re-tops the card, which put it back above the right-click panel that had
    // just been raised on purpose - the panel stayed underneath until it was closed.
    public static void PlaceWithoutActivating(IntPtr hwnd, WindowPlacement placement)
    {
        hwnd = RootWindow(hwnd);
        SetWindowPos(hwnd, IntPtr.Zero, placement.X, placement.Y, placement.Width, placement.Height,
            SwpNoZOrder | SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder);
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
