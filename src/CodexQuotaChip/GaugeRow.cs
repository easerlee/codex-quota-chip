namespace CodexQuotaChip;

// One overlay row: a short label, a bar filled to whatever is left of that window, and the
// percentage on the right - same size as the label, so the two rows read as one block.
public sealed class GaugeRow : FrameworkElement
{
    private const double LabelLeft = 8;
    private const double BarRightMargin = 8;
    // The gap between the bar and the percentage.
    private const double PercentGap = 4;

    private readonly string _label;
    private double? _remaining;
    private Settings _settings;

    public GaugeRow(string label, Settings settings)
    {
        _label = label;
        _settings = settings;
        SnapsToDevicePixels = true;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
    }

    public void Show(double? remaining, Settings settings)
    {
        _remaining = remaining;
        _settings = settings;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = Math.Max(28.0, ActualWidth);
        var height = Math.Max(12.0, ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var textSize = Readout.TextSize(height);
        var text = new SolidColorBrush(Theme.Text(_settings.Plate));

        var label = TextDrawing.Format(_label, textSize, FontWeights.Normal, text, dpi);
        label.MaxLineCount = 1;
        TextDrawing.DrawInk(dc, label, LabelLeft, height);

        // 右侧百分比：右边缘固定，和左边标签同字号、同颜色。列宽按最宽的「100%」算，所以两条
        // 进度条缩短的量一样 —— 数字位数变化不会让它们左右跳。
        var column = PercentWidth(height, dpi);
        var percent = TextDrawing.Format(Readout.Percent(_remaining), textSize, FontWeights.Normal, text, dpi);
        percent.MaxLineCount = 1;
        TextDrawing.DrawInk(
            dc, percent, width - BarRightMargin - percent.WidthIncludingTrailingWhitespace, height);

        var (left, right) = BarRange(width, height, column);
        var barWidth = right - left;
        if (barWidth < 6)
        {
            return;
        }

        // Whole-pixel geometry: the rows are ~14px tall on a 48px taskbar, so rounding keeps both
        // rows on the same pixel grid and their antialiasing identical.
        var barHeight = Math.Round(Math.Clamp(height * 0.42, 4.0, 7.0));
        var barTop = Math.Round((height - barHeight) / 2);
        const double Radius = 3.0;

        dc.DrawRoundedRectangle(
            new SolidColorBrush(Theme.Track(_settings.Plate)), null, new Rect(left, barTop, barWidth, barHeight), Radius, Radius);

        if (!_remaining.HasValue)
        {
            return;
        }

        var filled = Math.Clamp(barWidth * Math.Clamp(_remaining.Value, 0.0, 100.0) / 100.0, Radius * 2, barWidth);
        dc.DrawRoundedRectangle(
            new SolidColorBrush(Theme.ForRemaining(_remaining, _settings)),
            null,
            new Rect(left, barTop, filled, barHeight),
            Radius,
            Radius);
    }

    // 百分比列的宽度，按最宽的「100%」量。两条进度条都用这个值，所以缩短的量永远一致。
    public static double PercentWidth(double rowHeight, double pixelsPerDip) =>
        Math.Ceiling(TextDrawing.Format("100%", Readout.TextSize(rowHeight), FontWeights.Normal, Brushes.White, pixelsPerDip)
            .WidthIncludingTrailingWhitespace);

    // Both rows start their bar at the same x and end it at the same x. The range is derived from
    // the row geometry alone - never from the label's measured width - so 5H and WK always line up.
    public static (double Left, double Right) BarRange(double rowWidth, double rowHeight, double percentWidth) =>
        (LabelLeft + Math.Ceiling(Readout.TextSize(rowHeight) * 1.2) + 5,
         rowWidth - BarRightMargin - percentWidth - PercentGap);
}
