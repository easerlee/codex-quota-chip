namespace CodexQuotaChip;

// The overlay's bottom row: the reset clock and the weekly countdown, with room kept on the
// right for the status light.
public sealed class ResetRow : FrameworkElement
{
    private const double TextLeft = 8;
    private const double StatusLightRoom = 16;

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
