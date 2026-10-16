using System.Globalization;

namespace CodexQuotaChip;

// Text for the overlay is measured and drawn by hand: both bar rows must start at exactly the
// same x no matter how wide the label glyphs measure in whatever font ends up being used, and
// the rows must share one baseline.
public static class TextDrawing
{
    public const string UiFont = "YaHei Consolas Hybrid, Consolas, Segoe UI";

    public static FormattedText Format(string text, double size, FontWeight weight, Brush brush, double pixelsPerDip) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily(UiFont), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            pixelsPerDip);

    // Centres the glyph ink - not the line box - in the row, and puts the ink's left edge at x.
    public static void DrawInk(DrawingContext dc, FormattedText text, double x, double rowHeight)
    {
        var ink = text.BuildGeometry(new Point(0, 0)).Bounds;
        dc.DrawText(text, new Point(x - ink.Left, (rowHeight - ink.Height) / 2 - ink.Top));
    }
}
