namespace CodexQuotaChip;

// The overlay's bottom row: the reset clock and the weekly countdown, with room kept on the
// right for the status light.
public sealed class ResetRow : FrameworkElement
{
    private const double TextLeft = 8;
    private const double StatusLightRoom = 16;

    // The longest lines this row can ever be asked to show: the clock, then the countdown, and the
    // placeholders Readout falls back to when a window has no reset time at all. The card's width is
    // built from these - the bottom line is the one thing on the strip that must never be cut.
    private static readonly string[] WidestLines =
    [
        "23:59 / 13d 23h",
        "23:59 / 12h 59m",
        "23:59 / reset now",
        "--:-- / reset --",
        "已重置 / 已重置"
    ];

    // How wide one bottom-row line comes out, in the font the row actually draws with.
    public static double LineWidth(string line, double rowHeight, double pixelsPerDip) =>
        TextDrawing.Format(line, Readout.TextSize(rowHeight), FontWeights.Normal, Brushes.White, pixelsPerDip)
            .WidthIncludingTrailingWhitespace;

    // How wide the card has to be for the widest of them, with the room the status light keeps on
    // the right and the inset the text starts at.
    public static double RequiredWidth(double rowHeight, double pixelsPerDip)
    {
        var widest = 0.0;
        foreach (var line in WidestLines)
        {
            widest = Math.Max(widest, LineWidth(line, rowHeight, pixelsPerDip));
        }

        return TextLeft + widest + StatusLightRoom;
    }

    private string _text = string.Empty;
    private double _size;
    private bool _plate;

    public ResetRow()
    {
        SnapsToDevicePixels = true;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
    }

    public void Show(string text, double size, bool plate)
    {
        _text = text;
        _size = size;
        _plate = plate;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_text.Length == 0 || _size <= 0)
        {
            return;
        }

        var text = TextDrawing.Format(
            _text, _size, FontWeights.Normal, new SolidColorBrush(Theme.Text(_plate)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        text.MaxTextWidth = Math.Max(1.0, ActualWidth - TextLeft - StatusLightRoom);
        // One line, always: the strip is only a third of the taskbar, and a wrapped second line
        // would be drawn over the rows below. Too narrow means an ellipsis, never a second line.
        text.MaxLineCount = 1;
        text.Trimming = TextTrimming.CharacterEllipsis;
        TextDrawing.DrawInk(dc, text, TextLeft, Math.Max(12.0, ActualHeight));
    }
}
