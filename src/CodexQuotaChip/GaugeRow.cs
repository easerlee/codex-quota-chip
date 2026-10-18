namespace CodexQuotaChip;

// One overlay row: a short label and a bar filled to whatever is left of that window.
public sealed class GaugeRow : FrameworkElement
{
    private const double LabelLeft = 8;
    private const double BarRightMargin = 8;

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

        var label = TextDrawing.Format(_label, Readout.TextSize(height), FontWeights.Normal,
            new SolidColorBrush(Theme.Text(_settings.Plate)), dpi);
        label.MaxLineCount = 1;
        TextDrawing.DrawInk(dc, label, LabelLeft, height);

        var (left, right) = BarRange(width, height);
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

    // Both rows start their bar at the same x. The bar range is derived from the row geometry
    // alone - never from the label's measured width - so 5H and WK always line up.
    public static (double Left, double Right) BarRange(double rowWidth, double rowHeight) =>
        (LabelLeft + Math.Ceiling(Readout.TextSize(rowHeight) * 1.2) + 5, rowWidth - BarRightMargin);
}
