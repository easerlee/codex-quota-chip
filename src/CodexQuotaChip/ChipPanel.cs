using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Cursors = System.Windows.Input.Cursors;

namespace CodexQuotaChip;

// The right-click panel: a dark rounded card listing every account seen so far, each as a small
// chart, followed by the handful of settings this app has.
//
// Layout rules worth keeping:
//   * The card's own horizontal padding is 4px and every row insets itself by RowInset (12px).
//     That is deliberate: the scroll viewer clips its content at its own edge, so a hover plate
//     can only ever be drawn inside the viewport. With the inset on the rows, a row's plate
//     fills the viewport while its text still lands on the 5H column.
//   * The value column is measured from the widest text the panel will actually show, and the
//     reset column is a star column with a floor, so its right edge lands on the same edge the
//     sliding controls are aligned to.
public sealed class ChipPanel : Border
{
    private const double LabelWidth = 26;
    private const double BarWidth = 66;
    private const double MinValueWidth = 30;
    private const double MinResetWidth = 40;
    private const double RowInset = 12;
    private const double PlateHeight = 8;

    private static readonly FontFamily UiFont = new(TextDrawing.UiFont);

    private readonly StackPanel _rows = new();

    // The same class draws both layers: the card itself, and the settings layer that opens beside
    // it. The settings get a layer of their own so that unfolding them cannot resize - and so
    // cannot move - the card they were opened from.
    private readonly bool _settingsOnly;

    // What the last Fill was told, so the settings layer can rebuild itself without asking again.
    private IReadOnlyList<AccountRecord> _accounts = [];
    private string? _currentId;
    private Settings _settings = new();
    private bool _autostart;
    private readonly Action _refresh;
    private readonly Func<bool> _toggleAutostart;
    private readonly Func<bool> _toggleNotifications;
    private readonly Func<bool> _togglePlate;
    private readonly Action _openSettings;
    private readonly Action<int> _setRefreshSeconds;
    private readonly Action<string> _setDisplaySide;
    private readonly Action _exit;

    public ChipPanel(
        Action refresh,
        Func<bool> toggleAutostart,
        Func<bool> toggleNotifications,
        Func<bool> togglePlate,
        Action<int> setRefreshSeconds,
        Action<string> setDisplaySide,
        Action openSettings,
        Action exit,
        bool settingsOnly = false)
    {
        _settingsOnly = settingsOnly;
        _refresh = refresh;
        _toggleAutostart = toggleAutostart;
        _toggleNotifications = toggleNotifications;
        _togglePlate = togglePlate;
        _setRefreshSeconds = setRefreshSeconds;
        _setDisplaySide = setDisplaySide;
        _openSettings = openSettings;
        _exit = exit;

        Background = new SolidColorBrush(Theme.Panel.Background);
        BorderBrush = new SolidColorBrush(Theme.Panel.Outline);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(4, 12, 4, 12);
        // No drop shadow: it darkened the desktop around the rounded corners, which reads as a
        // grey block behind the card rather than as a transparent corner.
        // The panel grows with the number of accounts, so keep it on screen and let it scroll.
        MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 80);
        Child = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    // Rebuilt on every open, so numbers and switch states are never stale. Opening always lands on
    // the top page: the second level is for settings, not for staring at.
    public void Fill(IReadOnlyList<AccountRecord> accounts, string? currentId, Settings settings, bool autostart)
    {
        _accounts = accounts;
        _currentId = currentId;
        _settings = settings;
        _autostart = autostart;
        if (_settingsOnly)
        {
            BuildSettings();
            return;
        }

        BuildAccounts();
    }

    // Accounts first, then the two things worth reaching for. Everything that is a setting lives
    // in the column on the right.
    private void BuildAccounts()
    {
        _rows.Children.Clear();

        var now = DateTimeOffset.Now;
        var ordered = AccountOrder.Sort(_accounts, _currentId, now);
        if (ordered.Count == 0)
        {
            var empty = Caption("还没有账号记录，取数成功后自动记录", Theme.Panel.Dim, 12);
            empty.Margin = new Thickness(RowInset, 0, RowInset, 0);
            _rows.Children.Add(empty);
        }

        var valueWidth = MeasureColumn(ordered, now, MinValueWidth, 13, (remaining, resetsAt, at, _) => ValueText(remaining, resetsAt, at));
        var resetWidth = MeasureColumn(ordered, now, MinResetWidth, 12, ResetText);

        var first = true;
        foreach (var account in ordered)
        {
            if (!first)
            {
                _rows.Children.Add(Divider());
            }

            first = false;
            _rows.Children.Add(AccountHead(
                AccountIdentity.Label(account.Email, account.Id),
                AccountOrder.IsCurrent(account, _currentId),
                Freshness.IsLive(account.LastSeenAt, now, _settings.RefreshSeconds),
                new ResetCredits(account.ResetCredits ?? 0, account.ResetCreditsExpireAt),
                now));
            _rows.Children.Add(Metric("5H", account.FiveHourLeft, account.FiveHourResetsAt, now, _settings, weekly: false, valueWidth, resetWidth));
            _rows.Children.Add(Metric("WK", account.WeekLeft, account.WeekResetsAt, now, _settings, weekly: true, valueWidth, resetWidth));
        }

        _rows.Children.Add(Divider());
        _rows.Children.Add(CommandRow("刷新当前账号数据", _refresh));
        _rows.Children.Add(CommandRow("设置 ›", _openSettings));
        _rows.Children.Add(Divider());
        _rows.Children.Add(CommandRow("退出", _exit, danger: true));
        _rows.Children.Add(VersionRow());
    }

    // 面板底部写一行版本号。排查时第一件事就是确认「现在跑的是哪一版」——「改完没重启」这种事，
    // 看一眼面板就能排除。
    private static UIElement VersionRow()
    {
        var version = typeof(ChipPanel).Assembly.GetName().Version;
        var label = Caption(version is null ? "dev" : $"v{version.ToString(3)}", Theme.Panel.Dim, 10.5);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.Margin = new Thickness(RowInset, 2, RowInset, 0);
        return label;
    }

    private void BuildSettings()
    {
        _rows.Children.Clear();
        _rows.Children.Add(SettingRow("刷新间隔", IntervalPicker(_settings.RefreshSeconds)));
        _rows.Children.Add(SettingRow("显示位置", new Segmented(
            ["左", "右"], _settings.DisplayLeft ? 0 : 1, index => _setDisplaySide(index == 0 ? "left" : "right"))));
        // The plate the overlay draws under itself: same idea as this panel's own background.
        _rows.Children.Add(SwitchRow("黑色底图", _settings.Plate, _togglePlate));
        _rows.Children.Add(AutostartRow(_autostart));
        _rows.Children.Add(SwitchRow("额度重置时通知", _settings.NotifyOnReset, _toggleNotifications));
    }

    private Segmented IntervalPicker(int currentSeconds)
    {
        var offered = OfferedIntervals(currentSeconds);
        var labels = offered.Select(seconds => (seconds / 60).ToString()).ToList();
        return new Segmented(labels, offered.IndexOf(currentSeconds), index => _setRefreshSeconds(offered[index]));
    }

    // 1/5/15 minutes, plus whatever is active right now if it came from somewhere else (an
    // edited settings file), so a choice is always highlighted.
    private static List<int> OfferedIntervals(int currentSeconds)
    {
        var offered = new List<int> { 60, 300, 900 };
        if (currentSeconds > 0 && !offered.Contains(currentSeconds))
        {
            offered.Add(currentSeconds);
            offered.Sort();
        }

        return offered;
    }

    // Sits right after the name of the account that is logged in now, so the rows under it read as
    // "these numbers belong to the live one".
    public const string CurrentMarker = "【当前】";

    // The account name, with that account's reset tickets sitting on the right-hand edge of the
    // row - the same edge the metrics' reset column ends on. A right-aligned strip grows leftwards,
    // so a second ticket lands to the left of the first.
    private static UIElement AccountHead(string name, bool current, bool live, ResetCredits resets, DateTimeOffset now)
    {
        var row = new Grid { Margin = new Thickness(RowInset, 6, RowInset, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            // The link, not the login: green while the app is still hearing from this account,
            // grey once its reading has gone quiet - which is also how a revoked token shows up.
            // Which account is logged in is the marker's job, further right on this row.
            Fill = new SolidColorBrush(live ? Theme.Panel.Ok : Theme.Panel.Idle)
        });

        var label = Caption(name, Theme.Panel.Text, 13);
        label.FontWeight = FontWeights.SemiBold;
        label.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(label);

        if (current)
        {
            // Small and grey: a marker on the name, not a second name competing with it.
            var marker = Caption(CurrentMarker, Theme.Panel.Dim, 11);
            marker.VerticalAlignment = VerticalAlignment.Center;
            marker.Margin = new Thickness(5, 1, 0, 0);
            head.Children.Add(marker);
        }

        Grid.SetColumn(head, 0);
        row.Children.Add(head);

        // An expired ticket is one the server no longer counts, so it is not drawn either.
        if (resets.Available > 0 && !resets.Expired(now))
        {
            var tickets = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            for (var index = 0; index < resets.Available; index++)
            {
                tickets.Children.Add(new Image
                {
                    Source = ResetTicket.Value,
                    // The asset is 16x10; drawing it at any other size only softens it.
                    Width = 16,
                    Height = 10,
                    Margin = new Thickness(3, 0, 0, 0),
                    ToolTip = resets.Describe(now)
                });
            }

            Grid.SetColumn(tickets, 1);
            row.Children.Add(tickets);
        }

        return row;
    }

    // The ticket comes out of the assembly's resources (see the csproj), so a pack URI, loaded
    // once, frozen so it can be reused by every row.
    private static readonly Lazy<BitmapImage> ResetTicket = new(() =>
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/Assets/reset-credit.png", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    });

    private static UIElement Metric(
        string label,
        double? remaining,
        long? resetsAt,
        DateTimeOffset now,
        Settings settings,
        bool weekly,
        double valueWidth,
        double resetWidth)
    {
        var spent = Readout.Spent(remaining, resetsAt, now);
        var color = Theme.Panel.ForRemaining(remaining, settings);

        var row = new Grid { Margin = new Thickness(RowInset, 2, RowInset, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(valueWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelText = Caption(label, Theme.Panel.Dim, 12);
        labelText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(labelText, 0);
        row.Children.Add(labelText);

        // A spent window has just refilled, so its bar is full: "已重置" is a label, not an empty
        // chart. A full window is a good window, so it keeps the full-window green.
        var bar = Bar(BarPercent(remaining, resetsAt, now), spent ? Theme.Panel.Ok : color);
        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);

        var value = Caption(ValueText(remaining, resetsAt, now), spent ? Theme.Panel.Dim : color, 13);
        value.FontWeight = FontWeights.SemiBold;
        value.VerticalAlignment = VerticalAlignment.Center;
        value.TextAlignment = TextAlignment.Left;
        value.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(value, 2);
        row.Children.Add(value);

        var reset = Caption(ResetText(remaining, resetsAt, now, weekly), Theme.Panel.Dim, 12);
        reset.VerticalAlignment = VerticalAlignment.Center;
        // Right-aligned, so every reset time ends on the same edge as the sliding controls.
        reset.HorizontalAlignment = HorizontalAlignment.Right;
        reset.TextAlignment = TextAlignment.Right;
        reset.TextTrimming = TextTrimming.CharacterEllipsis;
        reset.MinWidth = resetWidth;
        Grid.SetColumn(reset, 3);
        row.Children.Add(reset);

        return row;
    }

    private static string ValueText(double? remaining, long? resetsAt, DateTimeOffset now) =>
        Readout.Spent(remaining, resetsAt, now) ? "已重置" : Readout.Percent(remaining);

    private static string ResetText(double? remaining, long? resetsAt, DateTimeOffset now, bool weekly) =>
        Readout.Spent(remaining, resetsAt, now) ? string.Empty : Readout.When(remaining, resetsAt, now, weekly);

    // How full a metric row's bar is. A window that has just refilled is full again, so a reset
    // reads as a full bar rather than as an empty chart - the overlay draws the same thing from
    // the reading itself, and this is what keeps the panel's two rows agreeing with it.
    public static double BarPercent(double? remaining, long? resetsAt, DateTimeOffset now) =>
        Readout.Spent(remaining, resetsAt, now) ? 100.0 : Math.Clamp(remaining ?? 0.0, 0.0, 100.0);

    // The widest text this panel will show in a column, so the column hugs its content instead
    // of leaving a gap. The same picker feeds the renderer, so the two cannot disagree.
    private static double MeasureColumn(
        IReadOnlyList<AccountRecord> accounts,
        DateTimeOffset now,
        double minimum,
        double size,
        Func<double?, long?, DateTimeOffset, bool, string> pick)
    {
        var brush = new SolidColorBrush(Theme.Panel.Text);
        var widest = minimum;
        foreach (var account in accounts)
        {
            foreach (var text in new[]
                     {
                         pick(account.FiveHourLeft, account.FiveHourResetsAt, now, false),
                         pick(account.WeekLeft, account.WeekResetsAt, now, true)
                     })
            {
                if (text.Length > 0)
                {
                    widest = Math.Max(widest, TextDrawing.Format(text, size, FontWeights.SemiBold, brush, 1.0).Width + 2);
                }
            }
        }

        return Math.Ceiling(widest);
    }

    private static UIElement Bar(double percent, Color color)
    {
        var track = new Border
        {
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Theme.Panel.Track)
        };

        // Star-sized columns give an exact proportional fill without any measuring.
        var fillHost = new Grid();
        fillHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(percent, 0, 100), GridUnitType.Star) });
        fillHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(100 - percent, 0, 100), GridUnitType.Star) });
        var fill = new Border
        {
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(color)
        };
        Grid.SetColumn(fill, 0);
        fillHost.Children.Add(fill);

        var host = new Grid
        {
            Width = BarWidth - 10,
            Height = 7,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        host.Children.Add(track);
        host.Children.Add(fillHost);
        return host;
    }

    // 刷新间隔 and 显示位置 share one shape: the label in the 5H column, the sliding control
    // flush right on the same edge as the reset time column above it.
    private static UIElement SettingRow(string label, FrameworkElement control)
    {
        var row = new Grid { Margin = new Thickness(RowInset, 9, RowInset, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth + BarWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var text = Caption(label, Theme.Panel.Text, 12.5);
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 0);
        row.Children.Add(text);

        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    // A command row: the whole row is clickable and highlights on hover. The plate is the row's
    // own background, which spans the viewport, so the text keeps a RowInset margin inside it.
    private static UIElement CommandRow(string text, Action action, bool danger = false)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(RowInset, PlateHeight, RowInset, PlateHeight),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = Caption(text, danger ? Theme.Panel.Fail : Theme.Panel.Text, 12.5)
        };
        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Theme.Panel.Hover);
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => action();
        return row;
    }

    // The autostart row keeps the label column of the rows above but ends in a switch, with no
    // hover plate: the switch itself is the affordance. The switch takes the state the registry
    // ended up in, not the intent of the click, and the panel stays open so it is seen moving.
    private UIElement AutostartRow(bool autostart) =>
        SwitchRow("开机自动启动", autostart, _toggleAutostart);

    private static UIElement SwitchRow(string text, bool on, Func<bool> toggle)
    {
        var control = new Switch(on);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth + BarWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = Caption(text, Theme.Panel.Text, 12.5);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);

        var host = new Border
        {
            Padding = new Thickness(RowInset, PlateHeight, RowInset, PlateHeight),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = row
        };
        host.MouseLeftButtonUp += (_, _) => control.Show(toggle());
        return host;
    }

    private static UIElement Divider() => new Border
    {
        Height = 1,
        Margin = new Thickness(RowInset, 8, RowInset, 8),
        Background = new SolidColorBrush(Theme.Panel.Outline)
    };

    private static TextBlock Caption(string text, Color color, double size) => new()
    {
        Text = text,
        FontFamily = UiFont,
        FontSize = size,
        Foreground = new SolidColorBrush(color)
    };
}
