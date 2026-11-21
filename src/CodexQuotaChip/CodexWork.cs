using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexQuotaChip;

// 「当前工作结束后关机」：判断的是 Codex 还有没有活在做事，不是窗口开没开、进程在不在。
//
// 证据只来自会话转录 ~/.codex/sessions/**/rollout-*.jsonl：一个回合开始是 task_started，
// 结束是同一个 turn_id 的 task_complete。缺了配对就是「还有回合开着」。
//
// 一条底线贯穿整个判定：**没有正面证据说明它在干活，才认定空闲；证据不全一律算忙。**
// 误关机的代价远大于晚关十分钟。
//
// 常量的出处见 C:\Users\easer\.dsh\dsh_wechat\codex-idle-watch-spec.md，它们不是拍出来的。
public static class CodexWork
{
    public const int DefaultIdleMinutes = 5;

    // 选最近 8 个会话看就够了：更早的文件不可能还是「正在干活」的那个。
    private const int NewestFiles = 8;

    // 只读尾巴。日志会涨到几百 MB 甚至几 GB（本机已经有 863 MB 的），全量解析既慢又没必要。
    private const long TailBytes = 400_000;

    // 尾巴里一条 turn 事件都没有时往回找：1 MB 一块，最多 24 MB。
    private const long BackScanChunk = 1_048_576;
    private const int BackScanChunks = 24;

    // 文件这么久没动，说明有写操作痕迹也不算在写代码了。
    private const double BusyWindowSeconds = 180;

    // 回合开着但文件静默这么久：进程已挂或回合被丢弃，不再阻塞关机。
    private const double HangSeconds = 1200;

    // 写操作的痕迹。命中任意一个就算「在改东西」，而不是在纯聊天。
    private static readonly string[] WriteTokens =
    [
        "apply_patch", "Set-Content", "Out-File", "Add-Content", "New-Item", "Copy-Item",
        "Move-Item", "Remove-Item", "git add", "git commit", "integration_r", "施工报告", "施工记录"
    ];

    // 这一轮是被额度掐断的，不是干活。
    private static readonly string[] QuotaTokens = ["quota", "rate limit", "usage limit", "额度", "out of credits"];

    private static readonly string[] HeartbeatTokens = ["<heartbeat>", "<automation_id>", "由已安排任务发送"];

    // 一份文件从尾巴里读出来的状态。
    public readonly record struct FileState(
        bool Open,
        bool Heartbeat,
        bool Write,
        bool Quota,
        bool LastComplete,
        bool SawTurn)
    {
        public static readonly FileState Empty = new(false, false, false, false, false, false);
    }

    // 一个 tick 的结论。Evidence 是给人看的：判定错了，用户能自己看出是哪个文件在顶。
    public sealed record WorkReading(bool Busy, string Tag, string Evidence, DateTimeOffset? LastActivity)
    {
        public static readonly WorkReading Idle = new(false, "idle", "no sessions", null);
    }

    public static WorkReading Inspect(string codexHome, DateTimeOffset now)
    {
        try
        {
            var files = RecentSessions(codexHome);
            if (files.Count == 0)
            {
                return WorkReading.Idle;
            }

            var heartbeatThreads = HeartbeatThreads(codexHome);
            var busy = false;
            var tag = "idle";
            var open = 0;
            DateTime newest = DateTime.MinValue;

            foreach (var file in files)
            {
                var state = Read(file.FullName);
                // 文件名里带某个活跃心跳线程的 id，这个文件就是那个定时任务在跑。
                var heartbeat = state.Heartbeat || heartbeatThreads.Any(id => file.Name.Contains(id, StringComparison.OrdinalIgnoreCase));
                state = state with { Heartbeat = heartbeat };

                var age = (now.UtcDateTime - file.LastWriteTimeUtc).TotalSeconds;
                var (fileBusy, fileTag) = Classify(state, age);
                if (state.Open)
                {
                    open++;
                }

                if (file.LastWriteTimeUtc > newest)
                {
                    newest = file.LastWriteTimeUtc;
                }

                if (fileBusy)
                {
                    busy = true;
                    // 第一条忙的证据就够说明问题，后面不再覆盖。
                    if (tag == "idle")
                    {
                        tag = fileTag;
                    }
                }
            }

            var age2 = (now.UtcDateTime - newest).TotalSeconds;
            var evidence = $"open={open} newest={age2:0}s tag={tag}";
            return new WorkReading(busy, tag, evidence, new DateTimeOffset(newest, TimeSpan.Zero));
        }
        catch (Exception)
        {
            // 读不到、解析炸了、状态未知：都算忙。这条不能省 —— 误关机的代价远大于晚关。
            return new WorkReading(true, "unknown", "probe failed", null);
        }
    }

    // 分类顺序不能乱，前面的优先。返回「算不算忙」和那个给人看的标签。
    public static (bool Busy, string Tag) Classify(FileState state, double ageSeconds)
    {
        var staleOpen = state.Open && ageSeconds >= HangSeconds;
        var inFlight = state.Open && ageSeconds < HangSeconds && !state.Heartbeat;
        var constructing = state.Heartbeat && state.Write
                           && ((state.Open && ageSeconds < HangSeconds)
                               || (ageSeconds < BusyWindowSeconds && !state.LastComplete));

        // 1. 被额度掐断的回合不是干活。
        if (state.Quota && !inFlight && !constructing)
        {
            return (false, "quota");
        }

        // 2. 回合开着但久到不像还在跑：挂死的残局。
        if (staleOpen)
        {
            return (false, "stale-open");
        }

        // 3. 心跳线程确实在写东西 —— 定时任务也会改代码，得算忙。
        if (constructing)
        {
            return (true, "hb-work");
        }

        // 4. 心跳线程只是醒来扫一眼，不算。
        if (state.Heartbeat && !state.Write)
        {
            return (false, "hb-poll");
        }

        // 5. 回合开着、文件还在动：在飞思考。
        if (inFlight)
        {
            return (true, "open");
        }

        // 6. 三分钟内写过文件，且命中写操作：在写代码。
        if (ageSeconds < BusyWindowSeconds && state.Write)
        {
            return (true, "write");
        }

        return (false, "idle");
    }

    // 读尾巴；尾巴里没有 turn 事件就回扫。
    public static FileState Read(string path)
    {
        var tail = ReadTail(path, TailBytes);
        var state = Scan(tail);
        if (state.SawTurn)
        {
            return state;
        }

        // 长回合输出几 MB 工具日志后，400 KB 的尾巴里可能一条 turn 事件都没有。
        // 这时不能直接判空闲 —— 往回找最近一次是 task_started 还是 task_complete。
        var (kind, foundAny, heartbeat) = ScanBack(path);
        if (foundAny)
        {
            return state with
            {
                Open = kind == "started",
                SawTurn = true,
                Heartbeat = state.Heartbeat || heartbeat
            };
        }

        // 24 MB 里连 turn 标记都没有：文件两分钟内还在写就当还在跑（保守），否则是旧日志。
        var age = (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds;
        return age < HangSeconds ? state with { Open = true } : state;
    }

    // 逐行解析尾巴，汇总五个标志。turn 用集合配对，同一个 turn_id 的 complete 才关得掉它。
    public static FileState Scan(string text)
    {
        var open = new HashSet<string>(StringComparer.Ordinal);
        var heartbeat = false;
        var write = false;
        var quota = false;
        var sawTurn = false;
        var lastComplete = false;

        foreach (var line in Lines(text))
        {
            foreach (var token in HeartbeatTokens)
            {
                if (line.Contains(token, StringComparison.Ordinal))
                {
                    heartbeat = true;
                    break;
                }
            }

            foreach (var token in WriteTokens)
            {
                if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    write = true;
                    break;
                }
            }

            foreach (var token in QuotaTokens)
            {
                if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    quota = true;
                    break;
                }
            }

            var (kind, turn) = Turn(line);
            if (kind == "started")
            {
                sawTurn = true;
                lastComplete = false;
                if (turn.Length > 0)
                {
                    open.Add(turn);
                }
                else
                {
                    // 没有 turn_id 的 task_started：用个占位键，保证「开着」不会丢。
                    open.Add("?");
                }
            }
            else if (kind == "complete")
            {
                sawTurn = true;
                lastComplete = true;
                if (turn.Length > 0 && !open.Remove(turn) && open.Contains("?"))
                {
                    open.Remove("?");
                }
            }
        }

        return new FileState(open.Count > 0, heartbeat, write, quota, lastComplete, sawTurn);
    }

    // 一行 JSON 里的 turn 事件：event_msg + payload.type=task_started / task_complete。
    // 手写解析而不是整行反序列化 —— 日志行可能有几 MB 的工具输出，反序列化毫无必要。
    private static (string Kind, string Turn) Turn(string line)
    {
        if (!line.Contains("\"task_started\"", StringComparison.Ordinal)
            && !line.Contains("\"task_complete\"", StringComparison.Ordinal))
        {
            return (string.Empty, string.Empty);
        }

        var kind = line.Contains("\"task_started\"", StringComparison.Ordinal) ? "started" : "complete";
        return (kind, Value(line, "turn_id"));
    }

    // 从一行文本里取 "key":"value"。只处理它需要的这一种形态。
    private static string Value(string line, string key)
    {
        var at = line.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (at < 0)
        {
            return string.Empty;
        }

        var colon = line.IndexOf(':', at);
        if (colon < 0)
        {
            return string.Empty;
        }

        var start = line.IndexOf('"', colon);
        if (start < 0)
        {
            return string.Empty;
        }

        var end = line.IndexOf('"', start + 1);
        return end <= start ? string.Empty : line[(start + 1)..end];
    }

    private static IEnumerable<string> Lines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }

    // 读文件末尾这么多字节。第一行可能是半截，丢掉 —— 半行 JSON 解析出来的东西不能信。
    public static string ReadTail(string path, long bytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = stream.Length;
        var take = Math.Min(bytes, length);
        var start = length - take;

        stream.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[take];
        var read = 0;
        while (read < take)
        {
            var got = stream.Read(buffer, read, (int)take - read);
            if (got <= 0)
            {
                break;
            }

            read += got;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        if (start > 0)
        {
            // 从中间开始读，第一行大概率是断的。
            var newline = text.IndexOf('\n');
            text = newline < 0 ? string.Empty : text[(newline + 1)..];
        }

        return text;
    }

    // 从尾部按块往回扫，直到找到最近的一次 turn 事件，判定它是 start 还是 complete。
    // 所有字节运算用 long：日志涨到 3 GB 时 int 会溢出，整个 tick 直接崩。
    public static (string Kind, bool FoundAny, bool Heartbeat) ScanBack(string path)
    {
        var heartbeat = false;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = stream.Length;
            var scanned = 0L;

            for (var chunk = 0; chunk < BackScanChunks && scanned < length; chunk++)
            {
                var take = Math.Min(BackScanChunk, length - scanned);
                var start = length - scanned - take;
                stream.Seek(start, SeekOrigin.Begin);

                var buffer = new byte[take];
                var read = 0;
                while (read < take)
                {
                    var got = stream.Read(buffer, read, (int)take - read);
                    if (got <= 0)
                    {
                        break;
                    }

                    read += got;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, read);
                var lastKind = string.Empty;
                foreach (var line in Lines(text))
                {
                    if (!heartbeat)
                    {
                        foreach (var token in HeartbeatTokens)
                        {
                            if (line.Contains(token, StringComparison.Ordinal))
                            {
                                heartbeat = true;
                                break;
                            }
                        }
                    }

                    // 同一块里后出现的更靠后，所以不断覆盖就是这一块的最后一个 turn 事件。
                    if (line.Contains("\"task_started\"", StringComparison.Ordinal))
                    {
                        lastKind = "started";
                    }
                    else if (line.Contains("\"task_complete\"", StringComparison.Ordinal))
                    {
                        lastKind = "complete";
                    }
                }

                if (lastKind.Length > 0)
                {
                    // 已经是最靠后的 turn 事件了，再往回只会有更早的。
                    return (lastKind, true, heartbeat);
                }

                scanned += take;
            }
        }
        catch (Exception)
        {
            return (string.Empty, false, false);
        }

        return (string.Empty, false, heartbeat);
    }

    private static List<FileInfo> Newest(string codexHome) => RecentSessions(codexHome);

    // 最近这几个会话文件，按修改时间倒序。--work-check 也用它列证据行。
    public static List<FileInfo> RecentSessions(string codexHome)
    {
        var sessions = Path.Combine(codexHome, "sessions");
        if (!Directory.Exists(sessions))
        {
            return [];
        }

        return Directory.EnumerateFiles(sessions, "rollout-*.jsonl", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(NewestFiles)
            .ToList();
    }

    // 活跃的心跳自动化各自的 target_thread_id。文件名里带它，就说明这个会话是那个定时任务。
    public static IReadOnlyList<string> HeartbeatThreads(string codexHome)
    {
        var ids = new List<string>();
        try
        {
            var automations = Path.Combine(codexHome, "automations");
            if (!Directory.Exists(automations))
            {
                return ids;
            }

            foreach (var file in Directory.EnumerateFiles(automations, "*.toml", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (!text.Contains("heartbeat", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("PAUSED", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("status = \"PAUSED\"", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (Match match in Regex.Matches(text, "target_thread_id\\s*=\\s*\"([^\"]+)\""))
                {
                    ids.Add(match.Groups[1].Value);
                }
            }
        }
        catch (Exception)
        {
            // 读不到不算忙也不算闲：退回「没有心跳线程」这条默认，文件自身的标签仍然管用。
        }

        return ids;
    }

    // 这一拍该不该把倒计时往下走。忙过就重置起点，不是扣减也不是累计。
    public static bool TimeToShutdown(DateTimeOffset? lastActivity, DateTimeOffset now, TimeSpan idle) =>
        lastActivity is { } at && now - at >= idle;

    // 状态行只说四件事：在查看、在工作、还有多久、正在关。
    // 判定证据（open=… tag=…）不塞进这一行 —— 那串东西会让文案随秒数变长变短，
    // 要核对判定用 --work-check。
    public static string Describe(bool enabled, WorkReading? reading, DateTimeOffset now, TimeSpan idle)
    {
        if (!enabled)
        {
            return Strings.ShutdownOff;
        }

        if (reading is null)
        {
            return Strings.ShutdownChecking;
        }

        if (reading.Busy)
        {
            return Strings.ShutdownBusy;
        }

        if (reading.LastActivity is not { } at)
        {
            return Strings.ShutdownChecking;
        }

        var left = (at + idle) - now;
        if (left <= TimeSpan.Zero)
        {
            return Strings.ShutdownAboutTo;
        }

        // 分钟向上取整：还剩 30 秒就显示「1 分钟后关机」，不出现「0 分钟后」。
        return Strings.ShutdownIn(Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)));
    }
}
