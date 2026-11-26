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

    // 悬停提示与浮窗之间的空隙。
    private const double ToolTipGap = 6;

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
    private int _stripWidth;
    private ToolTip? _tip;
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
    private bool _steppedAside;
    private bool _closing;

    // 「工作结束后关机」。armed 只在这次运行里由用户亲手打开，设置文件里留着上次的值也不自动兑现。
    private bool _armed;
    private DateTimeOffset? _idleSince;
    private DateTimeOffset? _workCheckedAt;
    private CodexWork.WorkReading? _work;

    // --shutdown-dry-run：走到「该关机了」只写日志，不真的执行。用来验证判定与开关，不怕误关。
    private readonly bool _shutdownDryRun;

    // 上一次切换这个开关的时间，用来吃掉重复的同一次点击。
    private DateTimeOffset? _shutdownToggledAt;

    // 状态行那句「不带点」的话，和当前画到第几个点（1..3）。点只是装饰，判定结论在 _shutdownBase 里。
    private readonly DispatcherTimer _dotsTimer = new();
    private string _shutdownBase = string.Empty;
    private int _dots = 1;

    public MainWindow(Paths paths, string codexHome, Settings settings, Log log, CodexExe codexExe, bool shutdownDryRun = false)
    {
        _paths = paths;
        _codexHome = codexHome;
        _settings = settings;
        _log = log;
        _shutdownDryRun = shutdownDryRun;
        _client = new QuotaClient(codexHome, log, codexExe.Resolve);
        _accounts = new AccountFile(paths.Accounts);

        // 上次运行留下的「开着」不算数：这是会关掉整台机器的开关，每次启动都要亲手打开。
        // 而且面板上那个开关读的就是这个字段 —— 不清掉的话，重启后开关显示「开」，实际上没人盯着，
        // 用户会以为它还在数。
        if (settings.ShutdownAfterWork)
        {
            settings.ShutdownAfterWork = false;
            SettingsFile.Save(paths.Settings, settings, log);
        }

        // 语言在建界面之前定下来：下面每一处文字都是从这里取的。
        Strings.Use(settings.Language);

        InitializeComponent();
        // As wide as the bottom row needs to be, and never a fixed 116: the clock and the weekly
        // countdown are what set it, and every pixel they do not need is bar.
        _stripWidth = StripWidth(TaskbarThickness(), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        Width = _stripWidth;
        Height = DefaultHeight;

        // A tool window with no taskbar button never shows a title on hover, so the two mouse
        // actions have to be a tooltip of their own. The window is not click-through - it keeps the
        // mouse messages it needs for this (and for the panel's click-outside rule).
        _tip = new ToolTip { Content = Strings.Tip };
        // 提示放在浮窗正上方，像系统自己的任务栏提示那样，而不是压在浮窗上 —— 压在浮窗上两边都看不清。
        _tip.PlacementTarget = this;
        _tip.Placement = PlacementMode.Custom;
        _tip.CustomPopupPlacementCallback = (popup, target, _) =>
            [new CustomPopupPlacement(
                new Point(Math.Max(0, (target.Width - popup.Width) / 2), -popup.Height - ToolTipGap),
                PopupPrimaryAxis.Horizontal)];
        ToolTip = _tip;
        ToolTipService.SetInitialShowDelay(this, 300);
        ToolTipService.SetShowDuration(this, 10000);

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

        // 状态行后面那三个点：1 → 2 → 3 → 1，只改文字，不重建面板。
        _dotsTimer.Interval = TimeSpan.FromMilliseconds(400);
        _dotsTimer.Tick += (_, _) => StepDots();

        // Runs only while the panel is open; see Dismiss for why the panel watches the mouse.
        _panelGuard.Interval = TimeSpan.FromMilliseconds(80);
        _panelGuard.Tick += (_, _) => CheckPanelDismiss();
    }

    // ---------------------------------------------------------------- the panel

    private void BuildPanel()
    {
        _panel = new ChipPanel(RefreshFromPanel, ToggleAutostart, ToggleNotifications, TogglePlate, SetRefreshSeconds, SetDisplaySide, SetLanguage, ForgetAccount, ToggleShutdownAfterWork, SetShutdownIdle, ShutdownStatus, ToggleSettingsLayer, Exit);
        _settingsPanel = new ChipPanel(RefreshFromPanel, ToggleAutostart, ToggleNotifications, TogglePlate, SetRefreshSeconds, SetDisplaySide, SetLanguage, ForgetAccount, ToggleShutdownAfterWork, SetShutdownIdle, ShutdownStatus, ToggleSettingsLayer, Exit, settingsOnly: true);

        _popup.PlacementTarget = this;
        _popup.Placement = PlacementMode.Custom;
        // Both layers stay open on their own terms: WPF's own click-outside closes a popup as soon
        // as a press lands anywhere outside it, which would take the card with it the moment the
        // settings layer was clicked. The window already watches the mouse for that (see Dismiss),
        // and it counts both rectangles as inside.
        _popup.StaysOpen = true;
        _popup.AllowsTransparency = true;
        // No fade: a menu should be there as soon as it is asked for, and a fade that gets stuck
        // leaves the whole panel half transparent.
        _popup.PopupAnimation = PopupAnimation.None;
        _popup.Child = _panel;
        _popup.CustomPopupPlacementCallback = PlacePanel;
        _popup.Opened += (_, _) =>
        {
            _panelOpen = true;
            // The panel is opened by a button release, so the next press is a fresh one.
            _menuButtonWasUp = true;
            _panelGuard.Start();
            ApplyLayers();
            KeepWithTaskbar();
        };
        _popup.Closed += (_, _) =>
        {
            _panelOpen = false;
            _panelGuard.Stop();
            _settingsPopup.IsOpen = false;
            KeepWithTaskbar();
        };

        _settingsPopup.PlacementTarget = this;
        _settingsPopup.Placement = PlacementMode.Custom;
        _settingsPopup.StaysOpen = true;
        _settingsPopup.AllowsTransparency = true;
        _settingsPopup.PopupAnimation = PopupAnimation.None;
        _settingsPopup.Child = _settingsPanel;
        _settingsPopup.CustomPopupPlacementCallback = PlaceSettings;
        _settingsPopup.Opened += (_, _) => ApplyLayers();
    }

    // 层级只有一条规则，而且每次都从头落实：先把浮窗提到最上，再把菜单提到浮窗之上。
    // 「同一次调用、先卡片后面板」才是保证 —— 顺序在调用里定死，就不存在谁先把谁顶上去的问题；
    // 之前那一版分两个分支走，只要有一次走错分支（比如面板开着却读到「没开」），卡片就会压上去。
    private void ApplyLayers()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        // 系统菜单压过来时不提卡片：那一刻它马上要被让位逻辑藏起来，提上去只会盖住菜单。
        if (!IsMenuOver())
        {
            Win32.RaiseToTop(_handle);
        }

        if (_popup.IsOpen || _settingsPopup.IsOpen)
        {
            RaisePanels();
        }

        // 悬停提示也是我们自己的窗口，同样排在卡片之上。卡片每秒重提一次，不提它就会被卡片压住
        // —— 截图里提示文字被额度条盖住就是这么来的。
        if (_tip is { IsOpen: true } && PresentationSource.FromVisual(_tip) is HwndSource tip)
        {
            Win32.RaiseToTop(tip.Handle);
        }
    }

    private void RaisePanels()
    {
        foreach (var panel in new[] { _panel, _settingsPanel })
        {
            if (panel is not null && PresentationSource.FromVisual(panel) is HwndSource source)
            {
                Win32.RaiseToTop(source.Handle);
            }
        }
    }

    // 兜底：任务栏刷新会把它拥有的浮窗甩到菜单前面，一秒一次的补提在肉眼上就是「压住」。
    // 面板开着时，80ms 的守候定时器顺带看一眼 —— 判断方式是「两者重叠的那一点，最上层是谁」，
    // 不用 z-order 邻居：中间随时可能夹进提示框、输入法之类的窗口，邻居一错就永远不触发。
    private void KeepPanelsInFront()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        foreach (var panel in new[] { _panel, _settingsPanel })
        {
            if (panel is not null && PresentationSource.FromVisual(panel) is HwndSource source
                && CardCoversPanel(source.Handle))
            {
                Win32.RaiseToTop(source.Handle);
            }
        }
    }

    // 面板和浮窗重叠的那一点，最上层是不是浮窗自己。
    private bool CardCoversPanel(IntPtr panelHandle)
    {
        if (!Win32.TryWindowRect(panelHandle, out var panel) || !Win32.TryWindowRect(_handle, out var card))
        {
            return false;
        }

        var left = Math.Max(panel.Left, card.Left);
        var right = Math.Min(panel.Right, card.Right);
        var top = Math.Max(panel.Top, card.Top);
        var bottom = Math.Min(panel.Bottom, card.Bottom);
        if (left >= right || top >= bottom)
        {
            return false; // 不重叠就没有谁压谁的问题
        }

        var at = Win32.WindowAt((left + right) / 2, (top + bottom) / 2);
        return at != IntPtr.Zero && Win32.RootWindow(at) == Win32.RootWindow(_handle);
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
        KeepWithTaskbar();
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

    // 面板不跟着鼠标走：它的下边贴着浮窗的上边，右（左对齐时是左）边和浮窗对齐。两者**永远不重叠** ——
    // 不重叠就不存在谁压住谁，这比任何 z-order 判断都稳。位置只由浮窗矩形算出，点浮窗哪个位置都一样。
    private CustomPopupPlacement[] PlacePanel(Size popup, Size target, Point offset)
    {
        if (_panelAnchor is null)
        {
            _panelAnchor = PanelAnchorFor(popup);
        }

        return [new CustomPopupPlacement(_panelAnchor.Value, PopupPrimaryAxis.Horizontal)];
    }

    private Point PanelAnchorFor(Size popup)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0)
        {
            scale = 1;
        }

        // 拿不到浮窗矩形（极少见）时退回到「面板左下角在浮窗左上角」，至少不会盖住它。
        if (!Win32.TryWindowRect(_handle, out var card))
        {
            return new Point(-popup.Width, -popup.Height);
        }

        var cardWidth = (card.Right - card.Left) / scale;
        var x = _settings.DisplayLeft ? 0 : cardWidth - popup.Width;
        var y = -popup.Height - (SettingsGap / scale);

        // 屏幕上方放不下时往下压，宁可贴着浮窗，也不让面板跑到屏幕外。
        var screenTop = Win32.MonitorBounds(_handle).Top;
        var wanted = card.Top + (y * scale);
        if (wanted < screenTop)
        {
            y += (screenTop - wanted) / scale;
        }

        return new Point(x, y);
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
        // Runs only while a panel is open, every 80 ms: the cheapest place for the layer upkeep too.
        KeepPanelsInFront();

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

        CheckShutdownAfterWork(now);
        Render();
    }

    // 每 5 秒探一次：阈值是分钟量级，碰这么多磁盘已经够了。
    // 判定规则全在 CodexWork 里（回合配对、回扫、心跳线程），这里只做计时与执行。
    private void CheckShutdownAfterWork(DateTimeOffset now)
    {
        if (!_armed)
        {
            return;
        }

        if (_workCheckedAt is null || now - _workCheckedAt.Value >= TimeSpan.FromSeconds(5))
        {
            _workCheckedAt = now;
            _work = CodexWork.Inspect(_codexHome, now);

            // 起点只认「我们自己观察到的安静」：忙就清零，闲就在第一次看到时记下。
            // 不能再用 reading.LastActivity 当起点 —— 那会把打开开关之前已经过去的安静算进来，
            // 于是开关一开就立刻满足阈值（0.2.47 就是这么误关机的）。
            _idleSince = CodexWork.Track(_idleSince, _work.Busy, now);

            if (_settingsPopup.IsOpen)
            {
                FillPanels();
            }

            // 面板开着时让那行字跟着走：只要改文字，不重建面板。
            _shutdownBase = ShutdownBase();
            _panel?.RefreshShutdownStatus(ShutdownStatus());
        }

        var idle = TimeSpan.FromMinutes(_settings.ShutdownIdleMinutes);
        if (_work is null || _work.Busy || !CodexWork.Elapsed(_idleSince, now, idle))
        {
            return;
        }

        _armed = false;
        _dotsTimer.Stop();
        _settings.ShutdownAfterWork = false;
        SettingsFile.Save(_paths.Settings, _settings, _log);

        if (_shutdownDryRun)
        {
            // 只留证据，不动这台机器。验证「该关的时候真会关、关掉之后真不关」全靠它。
            _log.Info($"would shut down: work idle for {idle.TotalMinutes:0} min (since {_idleSince:u}) [dry run]");
            return;
        }

        _log.Info($"work idle for {idle.TotalMinutes:0} min (since {_idleSince:u}), shutting down");
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("shutdown", "/s /t 60") { UseShellExecute = false });
            _notifier?.Show(Strings.ShutdownRow, Strings.ShutdownAboutTo);
        }
        catch (Exception error)
        {
            _log.Warn($"shutdown not started: {error.Message}");
        }
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

        // Before the new reading is filed, so a window that ran out of time while the app was shut
        // (or while the account was not the one being read) is still visible in the record.
        WatchRefills();

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

    // A window refills whether or not its account is the one being read - and the account nobody is
    // reading is exactly the one that used to go unannounced. So every record on file is checked
    // before the fresh reading is filed: a window whose reset time has gone by has refilled, and it
    // is reported once.
    //
    // The record itself is the marker that keeps it to once: the window is marked full and its
    // reset time dropped (the next one is not known until the server says so), so neither this nor
    // the jump check - which has nothing left to compare against - can fire for it again.
    private void WatchRefills()
    {
        var now = DateTimeOffset.Now;
        var currentId = AccountIdentity.Read(_codexHome)?.Id;
        var records = _accounts.Load().ToList();
        var changed = false;
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            var five = ResetWatch.Refilled(record.FiveHourLeft, record.FiveHourResetsAt, now);
            var week = ResetWatch.Refilled(record.WeekLeft, record.WeekResetsAt, now);
            if (!five && !week)
            {
                continue;
            }

            var label = AccountIdentity.Label(record.Email, record.Id);
            var live = string.Equals(record.Id, currentId, StringComparison.OrdinalIgnoreCase);
            if (_settings.NotifyOnReset)
            {
                var shown = live
                    ? _notifier?.Show(Strings.ResetToastTitle, new ResetRollover(five, week).Describe(100, 100)) ?? false
                    : _notifier?.Show(
                        Strings.RefillHeadline(label, five, week),
                        Strings.UnloggedNote) ?? false;

                _log.Info(shown
                    ? $"quota refill notified (account={label}, live={live}, 5h={five}, week={week})"
                    : $"quota refill not shown (account={label}, live={live})");
            }

            records[index] = record with
            {
                FiveHourLeft = five ? 100 : record.FiveHourLeft,
                FiveHourResetsAt = five ? null : record.FiveHourResetsAt,
                WeekLeft = week ? 100 : record.WeekLeft,
                WeekResetsAt = week ? null : record.WeekResetsAt
            };
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        foreach (var record in records)
        {
            _accounts.Upsert(record);
        }
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
            reading.Resets?.ExpiresAt,
            // 订阅到期日只在「这个账号是当前登录」时读得到（id_token 里），所以随记录存下来。
            identity.PlanUntil));

        if (rollover.Any && _settings.NotifyOnReset)
        {
            var shown = _notifier?.Show(Strings.ResetToastTitle, rollover.Describe(
                reading.FiveHour?.RemainingPercent, reading.Weekly?.RemainingPercent)) ?? false;
            _log.Info(shown
                ? $"quota reset notified (5h={rollover.FiveHour}, week={rollover.Week})"
                : $"quota reset not shown (5h={rollover.FiveHour}, week={rollover.Week})");
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

    // 换语言要立刻见效，而文字是构建时写进控件的 —— 所以两层都整块重建，顺带更新悬停提示。
    private void SetLanguage(string language)
    {
        _settings.Language = language;
        _settings.Normalize();
        SettingsFile.Save(_paths.Settings, _settings, _log);
        Strings.Use(_settings.Language);
        _tip!.Content = Strings.Tip;
        FillPanels();
    }

    private void FillPanels()
    {
        var accounts = _accounts.Load();
        var current = AccountIdentity.Read(_codexHome)?.Id;
        var autostart = Autostart.IsEnabled();
        _panel?.Fill(accounts, current, _settings, autostart);
        if (_settingsPopup.IsOpen)
        {
            _settingsPanel?.Fill(accounts, current, _settings, autostart);
        }
    }

    // 删掉一个账号的历史记录，只动 accounts.json。codex 的登录凭据这个程序从不写入，所以账号仍然
    // 处于登录状态；若它正好是当前登录的那个，下一轮刷新会重新记下它 —— 但面板不给当前账号画这个
    // ×，所以正常路径走不到这里。
    private void ForgetAccount(string id)
    {
        _accounts.Remove(id);
        _log.Info($"forgot account {id}");
        FillPanels();
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

    // 工作结束后关机。开着才会去看 Codex 动静；关掉 = 取消，什么都不做。
    // 这个会话里的开关状态另存一份，因为它是「同意关机」的意图，不该被上次运行留下的设置直接兑现。
    //
    // 250 毫秒防抖：开关行和它下面那行状态都能点到这件事（一个是开、一个是取消），而面板在切换后
    // 会重建、位置也可能挪动 —— 同一次点击被送进来两次时，来回翻转的结果是「看着点了却没反应」，
    // 对一个会关机的开关来说，这种不确定不能留。
    private bool ToggleShutdownAfterWork()
    {
        var now = DateTimeOffset.Now;
        if (_shutdownToggledAt is { } last && now - last < TimeSpan.FromMilliseconds(250))
        {
            return _settings.ShutdownAfterWork;
        }

        _shutdownToggledAt = now;
        _settings.ShutdownAfterWork = !_settings.ShutdownAfterWork;
        _settings.Normalize();
        SettingsFile.Save(_paths.Settings, _settings, _log);
        _armed = _settings.ShutdownAfterWork;
        // 每次打开都从零开始数：上一次的安静、上一次启动留下的判定，都不算数。
        // 关掉时同样清掉，免得下次打开继承一个已经过期的起点。
        _idleSince = null;
        _workCheckedAt = null;
        // 开着才有那句状态，点也只在开着时走；关掉就把定时器停了，别让它在后台空转。
        if (_armed)
        {
            _work = CodexWork.Inspect(_codexHome, DateTimeOffset.Now);
            _shutdownBase = ShutdownBase();
            _dotsTimer.Start();
        }
        else
        {
            _dotsTimer.Stop();
            _dots = 1;
        }

        _log.Info($"shutdown after work {(_settings.ShutdownAfterWork ? "armed" : "off")}");
        FillPanels();
        return _settings.ShutdownAfterWork;
    }

    // 开关下面那一行：还有多久关机，或者现在卡在哪。证据一起写出来 —— 判定错了，用户能自己看出
    // 是哪个会话在顶着。
    private string ShutdownStatus() => _shutdownBase + Dots.Of(_dots);

    // 不带点的结论，交给 _dotsTimer 往上添点。
    private string ShutdownBase() =>
        CodexWork.Describe(
            _armed,
            _work,
            _idleSince,
            DateTimeOffset.Now,
            TimeSpan.FromMinutes(_settings.ShutdownIdleMinutes));

    // 1 → 2 → 3 → 1。面板没开时也算一下，开面板第一次画出来的就是当前这一步。
    private void StepDots()
    {
        _dots = Dots.Next(_dots);
        _panel?.RefreshShutdownStatus(ShutdownStatus());
    }

    // 结束等待时长：改了就立刻按新的阈值算，不用重启。
    private void SetShutdownIdle(int minutes)
    {
        _settings.ShutdownIdleMinutes = minutes;
        _settings.Normalize();
        SettingsFile.Save(_paths.Settings, _settings, _log);
        FillPanels();
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
        KeepWithTaskbar();
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

        KeepWithTaskbar();
        Refresh();
    }

    private void OnRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => ShowPanel();

    // 这里不改层级（层级只有 ApplyLayers 一个实现点），只把窗口收编成任务栏的从属窗口，
    // 让它跟着任务栏走。
    private void KeepWithTaskbar()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        Win32.MakeOverlayWindow(_handle);
        Win32.Own(new TaskbarOwner(_handle));
    }

    private void Snap()
    {
        // While stepped aside the placement timer must not move the window, not even when the
        // taskbar comes back - the visibility timer is what brings it out again.
        if (_steppedAside)
        {
            return;
        }

        if (_handle != IntPtr.Zero)
        {
            Win32.Own(new TaskbarOwner(_handle));
        }

        _stripWidth = StripWidth(TaskbarThickness(), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var placement = ResolvePlacement(_stripWidth, _settings.DisplayLeft);
        if (_handle == IntPtr.Zero)
        {
            Left = placement.X;
            Top = placement.Y;
            return;
        }

        // 每拍从头落实层级：先浮窗，后面板。这就是那条规则的唯一实现点。
        ApplyLayers();

        if (Win32.TryWindowRect(_handle, out var current) &&
            current.Left == placement.X && current.Top == placement.Y &&
            current.Width == placement.Width && current.Height == placement.Height)
        {
            return;
        }

        Win32.PlaceWithoutActivating(_handle, placement);
    }

    // How thick the bar is right now. 48 is only the guess used before the first placement: a
    // taskbar with small buttons, or a two-row one, is not 48 - and rows laid out for a height the
    // strip does not have is how the bottom line gets squeezed.
    public static int TaskbarThickness() =>
        Win32.TryTaskbarRect(out var taskbar) && !taskbar.IsEmpty
            ? Math.Min(taskbar.Width, taskbar.Height)
            : DefaultHeight;

    // The strip is as wide as the longest line its bottom row can show, and every pixel the rows do
    // not need goes to the bars. Kept static so --check can report the width the app would really
    // take without building a window.
    public static int StripWidth(int stripHeight, double pixelsPerDip)
    {
        var rowHeight = (Math.Max(1, stripHeight) - (StripMarginY * 2)) / 3.0;
        var content = ResetRow.RequiredWidth(rowHeight, pixelsPerDip);
        // One pixel of rounding: the measurement is exact, the layout is not.
        return (int)Math.Ceiling(Math.Max(DefaultWidth, (StripMarginX * 2) + content + 1));
    }

    public static WindowPlacement ResolvePlacement(int preferredWidth, bool pinLeft, bool waitForAnchors = false)
    {
        var monitor = Win32.MonitorBounds(IntPtr.Zero);
        if (Win32.TryTaskbarRect(out var taskbar))
        {
            var anchors = Win32.Anchors(waitForAnchors);
            return Placement.Compute(
                Placement.InferEdge(taskbar, monitor.Width, monitor.Height),
                taskbar,
                preferredWidth,
                TaskbarThickness(),
                monitor.Width,
                monitor.Height,
                anchors.RightLeft,
                anchors.LeftRight,
                pinLeft);
        }

        return Placement.Fallback(preferredWidth, TaskbarThickness(), monitor.Width, monitor.Height);
    }

    private void UpdateVisibility()
    {
        // Three things take the strip away from under the card: a fullscreen app in front, an
        // auto-hidden bar sliding off the screen, and a shell menu opening over it. All are answered
        // the same way - step aside, come back when they are gone.
        if (IsParked() || IsMenuOver())
        {
            if (!_steppedAside)
            {
                _steppedAside = true;
                Hide();
            }

            return;
        }

        if (_steppedAside)
        {
            _steppedAside = false;
            Show();
            Snap();
        }

        KeepWithTaskbar();
    }

    private bool IsMenuOver() =>
        _handle != IntPtr.Zero
        && Win32.TryWindowRect(_handle, out var strip)
        && Win32.ShellMenuOver(_handle, strip);

    // Two things take the taskbar away from under the overlay: a fullscreen app in front, and an
    // auto-hidden bar sliding off the screen. Both are answered the same way - step aside, come back
    // when the bar is back.
    private bool IsParked()
    {
        if (IsFullscreenForeground())
        {
            return true;
        }

        if (!Win32.TryTaskbarRect(out var taskbar))
        {
            return false;
        }

        var monitor = Win32.MonitorBounds(IntPtr.Zero);
        return Placement.Retracted(taskbar, monitor.Width, monitor.Height);
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

        // 比的是「浮窗所在那块屏」，不是窗口自己那块屏：副屏上最大化的窗口盖的是副屏，主屏任务栏上的
        // 浮窗没被挡住，不该让位。用 MonitorBounds(root) 会把副屏的窗口也算成全屏。
        return Fullscreen.Covers(Win32.WindowClass(root), rect, Win32.MonitorBounds(_handle));
    }
}
