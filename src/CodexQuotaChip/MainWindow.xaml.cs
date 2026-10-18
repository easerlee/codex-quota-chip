using System.ComponentModel;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CodexQuotaChip;

// The always-on overlay. It lives on the taskbar strip: never activated, never in the alt-tab
// list, positioned in physical pixels, and rebuilt from the last reading once a second.
public partial class MainWindow : Window
{
    private const int DefaultWidth = 116;
    private const int DefaultHeight = 48;

    // The gap between the card and the settings layer that opens beside it.
    private const double SettingsGap = 8;

    // Root's Margin in MainWindow.xaml, repeated here because the strip's width is worked out before
    // there is a layout to read it from.
    private const double StripMarginX = 3;
    private const double StripMarginY = 1;

    private readonly Paths _paths;
    private readonly Log _log;
    private readonly Settings _settings;
    private readonly string _codexHome;
    private readonly QuotaClient _client;
    private readonly AccountFile _accounts;

    private readonly Border _plate;
    private readonly int _stripWidth;
    private readonly GaugeRow _fiveHour;
    private readonly GaugeRow _week;
    private readonly ResetRow _resetLine;
    private readonly Border _statusLight;
    private readonly Popup _popup = new();
    private readonly Popup _settingsPopup = new();
    private ChipPanel? _settingsPanel;

    // Where a layer was first put. WPF asks again whenever the popup's size changes, and answering
    // from the cursor each time made the card crawl upwards as it was clicked: the answer is worked
    // out once and handed back unchanged, so where a layer appears is where it stays.
    private Point? _panelAnchor;
    private Point? _settingsAnchor;

    private readonly DispatcherTimer _renderTimer = new();
    private readonly DispatcherTimer _visibilityTimer = new();
    private readonly DispatcherTimer _placementTimer = new();
    private readonly DispatcherTimer _panelGuard = new();

    private ChipPanel? _panel;
    private Notifier? _notifier;
    private IntPtr _handle;
    private QuotaReading? _reading;
    private string? _lastError;
    private DateTimeOffset? _lastSuccessAt;
    private DateTimeOffset _nextReadAt;
    private bool _readInFlight;
    private bool _pendingRead;
    private bool _panelOpen;
    private bool _menuButtonWasUp = true;
    private bool _hiddenForFullscreen;
    private bool _closing;

    public MainWindow(Paths paths, string codexHome, Settings settings, Log log, CodexExe codexExe)
    {
        _paths = paths;
        _codexHome = codexHome;
        _settings = settings;
        _log = log;
        _client = new QuotaClient(codexHome, log, codexExe.Resolve);
        _accounts = new AccountFile(paths.Accounts);

        InitializeComponent();
        // As wide as the bottom row needs to be, and never a fixed 116: the clock and the weekly
        // countdown are what set it, and every pixel they do not need is bar.
        _stripWidth = StripWidth(VisualTreeHelper.GetDpi(this).PixelsPerDip);
        Width = _stripWidth;
        Height = DefaultHeight;

        for (var row = 0; row < 3; row++)
        {
            Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        // Behind everything: the panel's own dark plate, with a tighter radius than the panel's
        // because the chip is only this tall. The panel's switch shows and hides it.
        _plate = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Theme.Panel.Background),
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(_plate, 3);
        Root.Children.Add(_plate);
        ApplyPlate();

        _fiveHour = AddGauge("5H", 0);
        _week = AddGauge("WK", 1);

        _resetLine = new ResetRow();
        Grid.SetRow(_resetLine, 2);
        Root.Children.Add(_resetLine);

        _statusLight = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
            Background = new SolidColorBrush(Theme.Idle(_settings.Plate)),
            IsHitTestVisible = false
        };
        Grid.SetRow(_statusLight, 2);
        Root.Children.Add(_statusLight);

        BuildPanel();
        StartTimers();
        Refresh();
    }

    private GaugeRow AddGauge(string label, int row)
    {
        var gauge = new GaugeRow(label, _settings);
        Grid.SetRow(gauge, row);
        Root.Children.Add(gauge);
        return gauge;
    }

    private void StartTimers()
    {
        _renderTimer.Interval = TimeSpan.FromSeconds(1);
        _renderTimer.Tick += (_, _) => Tick();
        _renderTimer.Start();

        _visibilityTimer.Interval = TimeSpan.FromMilliseconds(500);
        _visibilityTimer.Tick += (_, _) => UpdateVisibility();
        _visibilityTimer.Start();

        _placementTimer.Interval = TimeSpan.FromSeconds(1);
        _placementTimer.Tick += (_, _) => Snap();
        _placementTimer.Start();

        // Runs only while the panel is open; see Dismiss for why the panel watches the mouse.
        _panelGuard.Interval = TimeSpan.FromMilliseconds(80);
        _panelGuard.Tick += (_, _) => CheckPanelDismiss();
    }

    // ---------------------------------------------------------------- the panel

    private void BuildPanel()
    {
        _panel = new ChipPanel(RefreshFromPanel, ToggleAutostart, ToggleNotifications, TogglePlate, SetRefreshSeconds, SetDisplaySide, ToggleSettingsLayer, Exit);
        _settingsPanel = new ChipPanel(RefreshFromPanel, ToggleAutostart, ToggleNotifications, TogglePlate, SetRefreshSeconds, SetDisplaySide, ToggleSettingsLayer, Exit, settingsOnly: true);

        _popup.PlacementTarget = this;
        _popup.Placement = PlacementMode.Custom;
        // Both layers stay open on their own terms: WPF's own click-outside closes a popup as soon
        // as a press lands anywhere outside it, which would take the card with it the moment the
        // settings layer was clicked. The window already watches the mouse for that (see Dismiss),
        // and it counts both rectangles as inside.
        _popup.StaysOpen = true;
        _popup.AllowsTransparency = true;
        _popup.PopupAnimation = PopupAnimation.Fade;
        _popup.Child = _panel;
        _popup.CustomPopupPlacementCallback = PlacePanel;
        _popup.Opened += (_, _) =>
        {
            _panelOpen = true;
            // The panel is opened by a button release, so the next press is a fresh one.
            _menuButtonWasUp = true;
            _panelGuard.Start();
            RaisePanelAboveOverlay();
            RaiseTopmost();
        };
        _popup.Closed += (_, _) =>
        {
            _panelOpen = false;
            _panelGuard.Stop();
            _settingsPopup.IsOpen = false;
            RaiseTopmost();
        };

        _settingsPopup.PlacementTarget = this;
        _settingsPopup.Placement = PlacementMode.Custom;
        _settingsPopup.StaysOpen = true;
        _settingsPopup.AllowsTransparency = true;
        _settingsPopup.PopupAnimation = PopupAnimation.Fade;
        _settingsPopup.Child = _settingsPanel;
        _settingsPopup.CustomPopupPlacementCallback = PlaceSettings;
        _settingsPopup.Opened += (_, _) => RaisePanelAboveOverlay();
    }

    // The overlay window is topmost, so the panel has to be raised as well: a plain popup would
    // otherwise be drawn underneath the card wherever the two overlap.
    private void RaisePanelAboveOverlay()
    {
        foreach (var panel in new[] { _panel, _settingsPanel })
        {
            if (panel is not null && PresentationSource.FromVisual(panel) is HwndSource source)
            {
                Win32.RaiseWithoutActivating(source.Handle);
            }
        }
    }

    private void ShowPanel()
    {
        if (_panel is null)
        {
            return;
        }

        _panel.Fill(_accounts.Load(), AccountIdentity.Read(_codexHome)?.Id, _settings, Autostart.IsEnabled());
        _settingsPopup.IsOpen = false;
        _panelAnchor = null;
        _settingsAnchor = null;
        RaiseTopmost();
        _popup.IsOpen = true;
    }

    // 设置 in the card unfolds the second layer; the same row puts it away again. Neither card
    // moves while that happens.
    private void ToggleSettingsLayer()
    {
        if (_settingsPanel is null)
        {
            return;
        }

        if (_settingsPopup.IsOpen)
        {
            _settingsPopup.IsOpen = false;
            return;
        }

        _settingsPanel.Fill(_accounts.Load(), AccountIdentity.Read(_codexHome)?.Id, _settings, Autostart.IsEnabled());
        _settingsAnchor = null;
        _settingsPopup.IsOpen = true;
    }

    // The card is parked at the screen's right end, so the panel opens up and to the left of the
    // cursor. These offsets are relative to the card, so negative coordinates are normal.
    private CustomPopupPlacement[] PlacePanel(Size popup, Size target, Point offset)
    {
        if (_panelAnchor is null)
        {
            var cursor = System.Windows.Input.Mouse.GetPosition(this);
            _panelAnchor = new Point(cursor.X - popup.Width + 10, cursor.Y - popup.Height - 10);
        }

        return [new CustomPopupPlacement(_panelAnchor.Value, PopupPrimaryAxis.Horizontal)];
    }

    // The settings layer goes against the right edge of the card it was opened from, a little way
    // off it, with the two bottoms on the same line. That is read from the card's own window
    // rectangle rather than from the cursor, so it lands there whatever the card did with the point
    // it was given. Anchored once, like the card itself.
    private CustomPopupPlacement[] PlaceSettings(Size popup, Size target, Point offset)
    {
        if (_settingsAnchor is null)
        {
            _settingsAnchor = SettingsAnchorFor(popup);
        }

        // Nothing readable to line up with: let WPF place it where it likes instead of throwing.
        return _settingsAnchor is null
            ? []
            : [new CustomPopupPlacement(_settingsAnchor.Value, PopupPrimaryAxis.Horizontal)];
    }

    private Point? SettingsAnchorFor(Size popup)
    {
        if (_panel is null
            || !Win32.TryWindowRect(_handle, out var chip)
            || PresentationSource.FromVisual(_panel) is not HwndSource card
            || !Win32.TryWindowRect(card.Handle, out var cardRect))
        {
            return null;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0)
        {
            scale = 1;
        }

        // Window rectangles are physical pixels; the placement point is in the card's own units.
        return new Point(
            ((cardRect.Right - chip.Left) / scale) + SettingsGap,
            ((cardRect.Bottom - chip.Top) / scale) - popup.Height);
    }

    private void CheckPanelDismiss()
    {
        var pressed = Win32.MouseButtonDown();
        var wasUp = _menuButtonWasUp;
        _menuButtonWasUp = !pressed;
        if (_panel is null || !Dismiss.ShouldClose(pressed, wasUp, CursorInsidePanel()))
        {
            return;
        }

        _popup.IsOpen = false;
    }

    // The panel is a window of its own, so its rectangle and the cursor are both read in raw
    // screen pixels - no DPI conversion between the two. Both layers count: a press in the settings
    // layer belongs to the panel, not to the desktop.
    private bool CursorInsidePanel()
    {
        if (!Win32.TryCursor(out var cursor))
        {
            return true;
        }

        return Inside(_panel, cursor) || Inside(_settingsPanel, cursor);
    }

    private static bool Inside(FrameworkElement? panel, Win32.Point cursor)
    {
        if (panel is null || PresentationSource.FromVisual(panel) is not HwndSource source)
        {
            return false;
        }

        // A rectangle that cannot be read counts as inside: a transient failure must not close a
        // panel the user is working in.
        return !Win32.TryWindowRect(source.Handle, out var rect)
            || (cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom);
    }

    // ---------------------------------------------------------------- reading

    private void Refresh()
    {
        _nextReadAt = DateTimeOffset.Now.AddSeconds(_settings.RefreshSeconds);
        StartRead();
    }

    // Command rows close the panel first, the way a menu behaves.
    private void RefreshFromPanel()
    {
        _popup.IsOpen = false;
        Refresh();
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        if (now >= _nextReadAt)
        {
            _nextReadAt = now.AddSeconds(_settings.RefreshSeconds);
            StartRead();
        }

        Render();
    }

    private void StartRead(bool pendingIfBusy = true)
    {
        if (_readInFlight)
        {
            if (pendingIfBusy)
            {
                _pendingRead = true;
            }

            Render();
            return;
        }

        _readInFlight = true;
        Render();
        _ = Task.Run(() => _client.ReadAsync())
            .ContinueWith(task => Dispatcher.Invoke(() => OnReadDone(task)));
    }

    private void OnReadDone(Task<QuotaReading> task)
    {
        _readInFlight = false;
        var reading = task.IsCompletedSuccessfully
            ? task.Result
            : QuotaReading.Failed(task.Exception?.GetBaseException().Message ?? "read failed");

        if (reading.Ok)
        {
            _reading = reading;
            _lastError = null;
            _lastSuccessAt = reading.At;
            Remember(reading);
        }
        else
        {
            _lastError = reading.Error;
            if (_reading is null || !_reading.Ok)
            {
                _reading = reading;
            }
        }

        if (_pendingRead)
        {
            _pendingRead = false;
            _nextReadAt = DateTimeOffset.Now.AddSeconds(_settings.RefreshSeconds);
            StartRead(false);
        }

        Render();
    }

    // File this reading under whoever is logged in right now. Switching accounts later leaves the
    // record alone, so it keeps the last values seen while that account was in use.
    private void Remember(QuotaReading reading)
    {
        var identity = AccountIdentity.Read(_codexHome);
        if (identity is null)
        {
            return;
        }

        // Compare against the record already on file, before replacing it: a reset time that moved
        // forward means that window just refilled.
        var before = _accounts.Load().FirstOrDefault(
            record => string.Equals(record.Id, identity.Id, StringComparison.OrdinalIgnoreCase));
        var rollover = ResetWatch.Check(
            before?.FiveHourResetsAt, before?.WeekResetsAt, reading.FiveHour?.ResetsAt, reading.Weekly?.ResetsAt);

        _accounts.Upsert(new AccountRecord(
            identity.Id,
            identity.Email,
            reading.PlanType ?? identity.Plan,
            DateTimeOffset.Now,
            reading.FiveHour?.RemainingPercent,
            reading.FiveHour?.ResetsAt,
            reading.Weekly?.RemainingPercent,
            reading.Weekly?.ResetsAt,
            reading.Resets?.Available,
            reading.Resets?.ExpiresAt));

        if (rollover.Any && _settings.NotifyOnReset)
        {
            _notifier?.Show("Codex 额度已重置", rollover.Describe(
                reading.FiveHour?.RemainingPercent, reading.Weekly?.RemainingPercent));
            _log.Info($"quota reset notified (5h={rollover.FiveHour}, week={rollover.Week})");
        }
    }

    // ---------------------------------------------------------------- painting

    private void Render()
    {
        _fiveHour.Show(_reading?.FiveHour?.RemainingPercent, _settings);
        _week.Show(_reading?.Weekly?.RemainingPercent, _settings);

        var now = DateTimeOffset.Now;
        var rowHeight = Root.ActualHeight > 0 ? Root.ActualHeight / 3 : DefaultHeight / 3.0;
        _resetLine.Show(
            $"{Readout.When(_reading?.FiveHour?.RemainingPercent, _reading?.FiveHour?.ResetsAt, now, weekly: false)} / " +
            $"{Readout.When(_reading?.Weekly?.RemainingPercent, _reading?.Weekly?.ResetsAt, now, weekly: true)}",
            Readout.TextSize(rowHeight),
            _settings.Plate);

        _statusLight.Background = new SolidColorBrush(StatusColor());
        UpdateTitle();
    }

    private Color StatusColor()
    {
        if (_readInFlight || IsStale())
        {
            return Theme.Warn(_settings.Plate);
        }

        if (_reading is null)
        {
            return Theme.Idle(_settings.Plate);
        }

        if (_lastError is not null && _lastSuccessAt.HasValue)
        {
            return Theme.Warn(_settings.Plate);
        }

        return _reading.Error is not null && !_lastSuccessAt.HasValue ? Theme.Fail(_settings.Plate) : Theme.Ok(_settings.Plate);
    }

    private bool IsStale()
    {
        if (!_lastSuccessAt.HasValue)
        {
            return false;
        }

        return !Freshness.IsLive(_lastSuccessAt.Value, DateTimeOffset.Now, _settings.RefreshSeconds);
    }

    private void UpdateTitle()
    {
        var stamp = _lastSuccessAt?.ToString("HH:mm") ?? "--:--";
        var notes = new List<string>();
        if (_readInFlight) notes.Add("reading");
        if (_pendingRead) notes.Add("pending");
        if (IsStale()) notes.Add("stale");
        if (_lastError is not null) notes.Add("last read failed");
        if (_reading?.Error is null && _reading?.FiveHour is null) notes.Add("5h unavailable");
        if (notes.Count == 0) notes.Add("quota ok");

        Title = $"Codex Quota Chip | updated {stamp} | every {_settings.RefreshSeconds}s | {string.Join(" | ", notes)}";
    }

    // ---------------------------------------------------------------- settings

    private void SetRefreshSeconds(int seconds)
    {
        _settings.RefreshSeconds = seconds;
        _settings.Normalize();
        SettingsFile.Save(_paths.Settings, _settings, _log);
        _nextReadAt = DateTimeOffset.Now.AddSeconds(_settings.RefreshSeconds);
        UpdateTitle();
    }

    // Moving the card is the whole point of this setting, so apply it on the spot instead of
    // waiting for the next placement tick to notice.
    private void SetDisplaySide(string side)
    {
        _settings.DisplaySide = side;
        _settings.Normalize();
        SettingsFile.Save(_paths.Settings, _settings, _log);
        Snap();
    }

    // Returns the state the registry ended up in, so the switch shows the truth rather than the
    // intent of the click.
    private bool ToggleAutostart()
    {
        Autostart.SetEnabled(!Autostart.IsEnabled(), _log);
        return Autostart.IsEnabled();
    }

    // Unlike autostart this one is ours alone, so the switch just shows what got saved.
    private bool ToggleNotifications()
    {
        _settings.NotifyOnReset = !_settings.NotifyOnReset;
        _settings.Normalize();
        SettingsFile.Save(_paths.Settings, _settings, _log);
        return _settings.NotifyOnReset;
    }

    // Same deal, and the overlay follows on the spot instead of waiting for a restart.
    private bool TogglePlate()
    {
        _settings.Plate = !_settings.Plate;
        _settings.Normalize();
        SettingsFile.Save(_paths.Settings, _settings, _log);
        ApplyPlate();
        return _settings.Plate;
    }

    private void ApplyPlate() =>
        _plate.Visibility = _settings.Plate ? Visibility.Visible : Visibility.Collapsed;

    private void Exit()
    {
        _closing = true;
        Application.Current.Shutdown();
    }

    // ---------------------------------------------------------------- window

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(Snap, DispatcherPriority.ApplicationIdle);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        _notifier = new Notifier(_handle, _log);
        Win32.MakeOverlayWindow(_handle);
        Win32.Own(new TaskbarOwner(_handle));
        Snap();
        RaiseTopmost();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _renderTimer.Stop();
        _visibilityTimer.Stop();
        _placementTimer.Stop();
        _panelGuard.Stop();
        _popup.IsOpen = false;
        _notifier?.Dispose();

        if (!_closing)
        {
            _closing = true;
            Dispatcher.BeginInvoke(() => Application.Current.Shutdown(), DispatcherPriority.ApplicationIdle);
        }
    }

    // Left click refreshes on the spot; while the panel is open that click only dismisses it.
    private void OnLeftClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_panelOpen)
        {
            return;
        }

        RaiseTopmost();
        Refresh();
    }

    private void OnRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => ShowPanel();

    private void RaiseTopmost()
    {
        if (_panelOpen)
        {
            return;
        }

        Topmost = true;
        if (_handle != IntPtr.Zero)
        {
            Win32.MakeOverlayWindow(_handle);
            Win32.RaiseWithoutActivating(_handle);
        }
    }

    private void Snap()
    {
        // While parked for a fullscreen app the placement timer must not move the window, not
        // even when the taskbar itself disappears.
        if (_hiddenForFullscreen)
        {
            return;
        }

        if (_handle != IntPtr.Zero)
        {
            Win32.Own(new TaskbarOwner(_handle));
        }

        var placement = ResolvePlacement(_stripWidth, _settings.DisplayLeft);
        if (_handle == IntPtr.Zero)
        {
            Left = placement.X;
            Top = placement.Y;
            return;
        }

        if (Win32.TryWindowRect(_handle, out var current) &&
            current.Left == placement.X && current.Top == placement.Y &&
            current.Width == placement.Width && current.Height == placement.Height)
        {
            return;
        }

        Win32.PlaceWithoutActivating(_handle, placement);
    }

    // The strip is as wide as the longest line its bottom row can show, and every pixel the rows do
    // not need goes to the bars. Kept static so --check can report the width the app would really
    // take without building a window.
    public static int StripWidth(double pixelsPerDip)
    {
        var rowHeight = (DefaultHeight - (StripMarginY * 2)) / 3.0;
        var content = ResetRow.RequiredWidth(rowHeight, pixelsPerDip);
        // One pixel of rounding: the measurement is exact, the layout is not.
        return (int)Math.Ceiling(Math.Max(DefaultWidth, (StripMarginX * 2) + content + 1));
    }

    public static WindowPlacement ResolvePlacement(int preferredWidth, bool pinLeft)
    {
        var monitor = Win32.MonitorBounds(IntPtr.Zero);
        if (Win32.TryTaskbarRect(out var taskbar))
        {
            return Placement.Compute(
                Placement.InferEdge(taskbar, monitor.Width, monitor.Height),
                taskbar,
                preferredWidth,
                DefaultHeight,
                monitor.Width,
                monitor.Height,
                Win32.TrayLeftEdge(),
                pinLeft);
        }

        return Placement.Fallback(preferredWidth, DefaultHeight, monitor.Width, monitor.Height);
    }

    private void UpdateVisibility()
    {
        if (IsFullscreenForeground())
        {
            if (!_hiddenForFullscreen)
            {
                _hiddenForFullscreen = true;
                Hide();
            }

            return;
        }

        if (_hiddenForFullscreen)
        {
            _hiddenForFullscreen = false;
            Show();
            Snap();
        }

        RaiseTopmost();
    }

    private bool IsFullscreenForeground()
    {
        var foreground = Win32.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        var root = Win32.RootWindow(foreground);
        if (root == _handle || !Win32.TryWindowRect(root, out var rect))
        {
            return false;
        }

        return Fullscreen.Covers(Win32.WindowClass(root), rect, Win32.MonitorBounds(root));
    }
}
