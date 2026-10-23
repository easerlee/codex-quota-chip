namespace CodexQuotaChip;

// 界面上的字集中在这里。语言是设置项，切换后立刻重建面板 —— 所以这里是「当前语言 + 取词属性」，
// 不做 per-window 的本地化资源。日志和命令行输出保持英文，它们不是给最终用户看的界面。
public static class Strings
{
    private static bool _english;

    public static bool English => _english;

    public static void Use(string? language) =>
        _english = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);

    private static string T(string zh, string en) => _english ? en : zh;

    // ---------------------------------------------------------------- 卡片

    public static string Tip => T("左键刷新.右键设置", "Left click: refresh · Right click: settings");

    // ---------------------------------------------------------------- 面板

    public static string RefreshNow => T("刷新当前账号数据", "Refresh now");

    public static string SettingsRow => T("设置 ›", "Settings ›");

    public static string Exit => T("退出", "Exit");

    public static string Current => T("【当前】", "[now]");

    public static string NoAccounts =>
        T("还没有账号记录，取数成功后自动记录", "No accounts yet — recorded after the first successful read");

    public static string ResetLabel => T("已重置", "reset");

    // ---------------------------------------------------------------- 设置层

    public static string RefreshInterval => T("刷新间隔", "Refresh");

    public static string DisplaySide => T("显示位置", "Side");

    public static string SideLeft => T("左", "L");

    public static string SideRight => T("右", "R");

    public static string Plate => T("黑色底图", "Dark plate");

    public static string Autostart => T("开机自动启动", "Autostart");

    public static string NotifyOnReset => T("额度重置时通知", "Reset alerts");

    public static string Language => T("语言", "Language");

    // ---------------------------------------------------------------- 重置券 / 通知

    public static string Credits(int count) => English
        ? $"{count} reset credit{(count == 1 ? string.Empty : "s")} available"
        : $"{count} 次额度重置可用";

    public static string CreditsExpire(string date) =>
        English ? $" (nearest expires {date})" : $"（最近 {date} 到期）";

    public static string ResetToastTitle => T("Codex 额度已重置", "Codex quota reset");

    public static string NotifyTestBody => T(
        "测试通知：看到这条就说明重置提醒能弹出来",
        "Test notification: if you can read this, reset alerts are working");

    public static string UnloggedNote => T(
        "该账号未登录，以最后一次刷新数据推算。",
        "That account is signed out; this is the last reading it produced.");

    public static string RefillHeadline(string label, bool five, bool week) => English
        ? $"{label}: {(five && week ? "5-hour and weekly quotas are back" : five ? "the 5-hour quota is back" : "the weekly quota is back")}"
        : $"「{label}」{(five && week ? "5 小时和周额度已恢复" : five ? "5 小时额度已恢复" : "周额度已恢复")}";

    // 通知正文里那句「现在还剩多少」。
    public static string Left(double? value) => value is null
        ? T("额度已恢复", "quota restored")
        : English
            ? $"{(int)Math.Round(value.Value)}% left"
            : $"现在剩余 {(int)Math.Round(value.Value)}%";

    public static string BothReset(string left) =>
        English ? $"5-hour and weekly quotas both reset, {left}" : $"5 小时和周额度都已重置，{left}";

    public static string FiveHourReset(string left) =>
        English ? $"5-hour quota reset, {left}" : $"5 小时额度已重置，{left}";

    public static string WeekReset(string left) =>
        English ? $"Weekly quota reset, {left}" : $"周额度已重置，{left}";
}
