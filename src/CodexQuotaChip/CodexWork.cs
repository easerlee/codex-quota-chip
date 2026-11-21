using System.Text.Json;

namespace CodexQuotaChip;

// 「当前工作结束后关机」：判断的是 Codex 还有没有活在做事，不是窗口开没开。
//
// 只读 ~/.codex 下的会话 rollout 文件，不碰 Codex 自己的任何东西。
//
// 自动任务是持续刷新的定时任务：开着的话 Codex 永远不会「结束」，直接判断会永远等下去，
// 所以这类工作默认不算数（但要在面板上说出来，不能装作没看见）。
public static class CodexWork
{
    public const int DefaultIdleMinutes = 5;

    // 最后一次真在做事的时间：新出现的会话文件或者最近一次写入。
    public static DateTimeOffset? LastActivity(string codexHome)
    {
        try
        {
            var sessions = Path.Combine(codexHome, "sessions");
            if (!Directory.Exists(sessions))
            {
                return null;
            }

            DateTime best = DateTime.MinValue;
            foreach (var file in Directory.EnumerateFiles(sessions, "*.jsonl", SearchOption.AllDirectories))
            {
                var written = File.GetLastWriteTimeUtc(file);
                if (written > best)
                {
                    best = written;
                }
            }

            return best == DateTime.MinValue ? null : new DateTimeOffset(best, TimeSpan.Zero);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // 心跳自动任务开着 = Codex 会一直被叫醒，永远等不到「结束」。
    public static bool ScheduledKeepAlive(string codexHome)
    {
        try
        {
            var automations = Path.Combine(codexHome, "automations");
            if (!Directory.Exists(automations))
            {
                return false;
            }

            foreach (var file in Directory.EnumerateFiles(automations, "*.toml", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (text.Contains("kind", StringComparison.OrdinalIgnoreCase)
                    && text.Contains("heartbeat", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("PAUSED", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // 这一拍该不该把倒计时往下走。
    public static bool TimeToShutdown(DateTimeOffset? lastActivity, DateTimeOffset now, TimeSpan idle) =>
        lastActivity is { } at && now - at >= idle;

    public static string Describe(
        bool enabled,
        DateTimeOffset? lastActivity,
        DateTimeOffset now,
        TimeSpan idle,
        bool scheduledKeepAlive)
    {
        if (!enabled)
        {
            return Strings.ShutdownOff;
        }

        if (scheduledKeepAlive)
        {
            return Strings.ShutdownWaitingScheduled;
        }

        if (lastActivity is not { } at)
        {
            return Strings.ShutdownNoActivity;
        }

        var left = (at + idle) - now;
        return left <= TimeSpan.Zero ? Strings.ShutdownAboutTo : Strings.ShutdownIn(Readout.Duration(left));
    }
}
