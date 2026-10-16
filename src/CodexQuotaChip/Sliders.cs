using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Cursors = System.Windows.Input.Cursors;

namespace CodexQuotaChip;

// The panel's sliding options, drawn in the same two colours as the charts above them: the plate
// takes the bar's track colour and the moving block the bar's green.
//
// One option is a fixed 25px wide, and the chosen block is a full-width child of the first
// column, so sliding it by exactly one column width lands it on the next option - no measuring,
// and no drift if the panel is resized.
public sealed class Segmented : Border
{
    private const double OptionWidth = 25;
    private const double ControlHeight = 22;
    private const double Inset = 2;
    private const int SlideMilliseconds = 150;

    private readonly TranslateTransform _shift = new();
    private readonly List<TextBlock> _labels = [];
    private readonly Action<int> _chosen;
    private int _selected;

    public Segmented(IReadOnlyList<string> options, int selected, Action<int> chosen)
    {
        _chosen = chosen;
        _selected = Math.Clamp(selected, 0, Math.Max(0, options.Count - 1));

        Width = OptionWidth * Math.Max(1, options.Count);
        Height = ControlHeight;
        CornerRadius = new CornerRadius(ControlHeight / 2);
        Background = new SolidColorBrush(Theme.Panel.Track);
        Padding = new Thickness(Inset);

        var grid = new Grid();
        for (var index = 0; index < options.Count; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var block = new Border
        {
            CornerRadius = new CornerRadius((ControlHeight - 2 * Inset) / 2),
            Background = new SolidColorBrush(Theme.Panel.Ok),
            RenderTransform = _shift
        };
        grid.Children.Add(block);

        for (var index = 0; index < options.Count; index++)
        {
            var label = Caption(options[index], index == _selected ? Theme.Panel.Background : Theme.Panel.Text, 11.5);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            _labels.Add(label);

            // Transparent still hit-tests, which is what makes the whole segment clickable.
            var segment = new Border
            {
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = label
            };
            var captured = index;
            segment.MouseLeftButtonUp += (_, _) => Select(captured);
            Grid.SetColumn(segment, index);
            grid.Children.Add(segment);
        }

        Child = grid;
        SizeChanged += (_, _) => Slide(animate: false);
    }

    private static TextBlock Caption(string text, Color color, double size) => new()
    {
        Text = text,
        FontFamily = new FontFamily(TextDrawing.UiFont),
        FontSize = size,
        Foreground = new SolidColorBrush(color)
    };

    private double ColumnWidth => Math.Max(1, (ActualWidth - 2 * Inset) / Math.Max(1, _labels.Count));

    private void Select(int index)
    {
        if (index == _selected)
        {
            return;
        }

        _selected = index;
        Slide(animate: true);
        for (var option = 0; option < _labels.Count; option++)
        {
            _labels[option].Foreground = new SolidColorBrush(
                option == _selected ? Theme.Panel.Background : Theme.Panel.Text);
        }

        _chosen(index);
    }

    private void Slide(bool animate)
    {
        var target = _selected * ColumnWidth;
        if (!animate)
        {
            _shift.BeginAnimation(TranslateTransform.XProperty, null);
            _shift.X = target;
            return;
        }

        _shift.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(SlideMilliseconds))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
    }
}

// The autostart switch: same plate colour, green knob as the moving option, so on/off reads as
// position plus colour instead of a tick tacked onto the label.
public sealed class Switch : Border
{
    private const double TrackWidth = 34;
    private const double TrackHeight = 18;
    private const double KnobSize = 14;
    private const double Inset = 2;
    private const int SlideMilliseconds = 150;

    private readonly TranslateTransform _shift = new();
    private readonly Ellipse _knob = new()
    {
        Width = KnobSize,
        Height = KnobSize,
        Margin = new Thickness(Inset, 0, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center
    };

    public Switch(bool on)
    {
        Width = TrackWidth;
        Height = TrackHeight;
        CornerRadius = new CornerRadius(TrackHeight / 2);
        Background = new SolidColorBrush(Theme.Panel.Track);
        _knob.RenderTransform = _shift;
        Child = _knob;
        Show(on, animate: false);
    }

    public void Show(bool on, bool animate = true)
    {
        _knob.Fill = new SolidColorBrush(on ? Theme.Panel.Ok : Theme.Panel.Idle);

        var target = on ? TrackWidth - KnobSize - 2 * Inset : 0;
        if (!animate)
        {
            _shift.BeginAnimation(TranslateTransform.XProperty, null);
            _shift.X = target;
            return;
        }

        _shift.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(SlideMilliseconds))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
    }
}
