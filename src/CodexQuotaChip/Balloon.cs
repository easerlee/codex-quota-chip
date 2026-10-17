using System.Runtime.InteropServices;

namespace CodexQuotaChip;

// A notification-area balloon, used when the toast API is unavailable.
//
// The shell only raises a balloon for an icon registered in the notification area, so one is
// registered for the life of the process. It is left visible rather than hidden: the shell does
// not raise balloons for an icon it is keeping out of sight, and a new icon is put in the
// overflow flyout anyway, which is why this is the fallback and not the primary path.
public sealed class Balloon : IDisposable
{
    private const uint IconId = 0x51;
    private const uint CallbackMessage = 0x0400 + 1; // WM_APP + 1, never handled: the icon is hidden
    private const int IdiInformation = 32516;

    private const uint NimAdd = 0x0;
    private const uint NimModify = 0x1;
    private const uint NimDelete = 0x2;
    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;
    private const uint NifInfo = 0x10;
    private const uint NiifInfo = 0x1;

    private readonly IntPtr _handle;
    private readonly Log _log;
    private bool _disposed;

    public Balloon(IntPtr handle, Log log)
    {
        _handle = handle;
        _log = log;
    }

    public bool Show(string title, string text)
    {
        if (_disposed || _handle == IntPtr.Zero)
        {
            return false;
        }

        var data = new NotifyIconData
        {
            Size = Marshal.SizeOf<NotifyIconData>(),
            Window = _handle,
            Id = IconId,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = CallbackMessage,
            Icon = LoadIcon(IntPtr.Zero, (IntPtr)IdiInformation),
            Tip = "Codex Quota Chip"
        };

        // Registering an icon that is already there just updates it, which is what makes this
        // survive the shell restarting underneath us.
        var added = ShellNotifyIcon(NimAdd, ref data);

        data.Flags = NifInfo;
        data.InfoTitle = title;
        data.Info = text;
        data.InfoFlags = NiifInfo;
        var shown = ShellNotifyIcon(NimModify, ref data);
        if (!shown)
        {
            _log.Warn(added
                ? $"notification not shown (win32 {Marshal.GetLastWin32Error()})"
                : "notification area unavailable, notification not shown");
        }

        return shown;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var data = new NotifyIconData
        {
            Size = Marshal.SizeOf<NotifyIconData>(),
            Window = _handle,
            Id = IconId,
            Tip = string.Empty,
            Info = string.Empty,
            InfoTitle = string.Empty
        };
        ShellNotifyIcon(NimDelete, ref data);
    }

    // The pre-Vista layout, which is still accepted and is sized exactly by cbSize: everything the
    // shell reads, and nothing it would have to guess at.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
}
