namespace CodexQuotaChip;

// 「正在查看…」后面那三个点：1 → 2 → 3 → 1。放在这里而不是计时器里，是为了能直接测。
public static class Dots
{
    public const int Max = 3;

    public static int Next(int current) => current >= Max ? 1 : current + 1;

    public static string Of(int count) => new('.', Math.Clamp(count, 1, Max));
}
