using System.Runtime.InteropServices;

namespace CodexQuotaChip;

// 用户在不在动键盘鼠标。关机判定必须同时看这个：
// 只盯 Codex 会话的话，人在别的工具里写东西、Codex 恰好闲着，就会被判成「干完了」而关机 ——
// 这已经真实发生过一次。
public static class Input
{
    public static TimeSpan Idle()
    {
        try
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info))
            {
                return TimeSpan.Zero;
            }

            // dwTime 是 32 位 tick，溢出回绕是常态，所以整段按未检查算术算。
            var elapsed = unchecked(Environment.TickCount - (int)info.Time);
            return elapsed <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(elapsed);
        }
        catch (Exception)
        {
            // 读不到就当「人还在动」—— 宁可晚关，也不能把人家正在写的东西关掉。
            return TimeSpan.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}
