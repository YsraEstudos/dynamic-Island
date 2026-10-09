using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using Island.App.Animations;
using Island.App.ViewModels;
using Island.Core.Configuration;
using Island.Core.Models;
using Island.Windows.Display;
using Island.Windows.Shell;

namespace Island.App.Shell;

/// <summary>
/// Borderless, transparent, non-activating overlay that hosts the island. The window is a fixed 1120 x 330 DIP
/// canvas (wide enough for the widest shelf); only the island shape takes input.
/// </summary>
public partial class IslandWindow : System.Windows.Window
{
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    private readonly IslandViewModel _vm;
    private readonly MonitorService _monitors;
    private readonly Func<IslandSettings> _settings;
    private readonly IslandTransitions _transitions;
    private readonly IslandContextMenu _menu;
    private readonly OutsideClickWatcher _menuOutsideClicks;
    private readonly Action<IslandSettings>? _saveSettings;
    private readonly Func<UpdateInfo?> _availableUpdate;
    private System.Windows.Interop.HwndSource? _hwndSource;

    // Pointer over the island and an open context menu both hold temporary states; the last value sent is kept.
    private bool _pointerInside;
    private bool _menuOpen;
    private bool _dragActive;
    private bool _interactionSent;

    // After the pointer leaves the shelf or clipboard, the island closes quickly instead of waiting out the idle timer.
    private static readonly TimeSpan LeaveCollapseDelay = TimeSpan.FromMilliseconds(250);
    private System.Windows.Threading.DispatcherTimer _leaveTimer = null!;

    // Hold-and-drag: a press that stays within HoldSlop pixels for HoldDuration turns into a drag of the island.
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMilliseconds(350);
    private const double HoldSlop = 4.0;
    private readonly System.Windows.Threading.DispatcherTimer _holdTimer;
    private bool _holdPending;
    private bool _dragging;
    private bool _dragMoved;
    private bool _suppressClick;
    private double _pressX;
    private double _pressY;
    // Pointer offset from the island's top-centre when the drag began (physical pixels).
    private double _grabX;
    private double _grabY;
    // Last physical top-centre of the island during a drag; saved on release. Docked, X is the pointer and Y the centre.
    private int _dragX;
    private int _dragY;

    // Throw to Mini: recent pointer samples of a drag (physical px, Environment.TickCount64 ms). A fast rise on release minimises.
    private const long ThrowBufferMs = 150;
    private const long ThrowWindowMs = 120;
    private const long ThrowMinSpanMs = 30;
    private const double ThrowSpeedDip = 1000.0;
    private readonly List<(long Time, double X, double Y)> _dragSamples = new();

    // Edge the island is docked to. The only source for alignment, layer, shape and placement.
    private DockEdge _dock = DockEdge.None;
    // Dock thresholds in DIP (scaled by the monitor's DPI). Leaving an edge needs a longer move than entering it.
    private const double DockEnterDip = 48.0;
    private const double DockLeaveDip = 96.0;
    private const double DockMarginDip = 8.0;

    public IslandWindow(IslandViewModel vm, MonitorService monitors, Func<IslandSettings> settings,
        Action<IslandSettings>? saveSettings = null, Func<UpdateInfo?>? availableUpdate = null)
    {
        InitializeComponent();

        _vm = vm;
        _monitors = monitors;
        _settings = settings;
        _saveSettings = saveSettings;
        _availableUpdate = availableUpdate ?? (() => null);
        DataContext = vm;

        IslandSettings initial = settings();
        _transitions = new IslandTransitions(
            IslandShape,
            RootGrid,
            new System.Windows.UIElement[]
            {
                CompactLayer, VolumeLayer, VerticalCompactLayer, VerticalVolumeLayer,
                PreviewLayer, NoticeLayer, ShelfLayer, ClipboardLayer, MiniLayer,
            },
            initial.ReduceAnimations);

        PreviewLayer.Attach(vm);
        NoticeLayer.Attach(vm);
        ShelfLayer.Attach(vm);
        ClipboardLayer.Attach(vm);
        ShelfLayer.LayoutChanged += OnShelfLayoutChanged;
        ShelfLayer.DragActiveChanged += OnShelfDragActiveChanged;

        _menu = new IslandContextMenu();
        _menu.Bind(vm);
        _menu.SettingsRequested += () => OpenSettingsRequested?.Invoke();
        _menu.QuitRequested += () => QuitRequested?.Invoke();
        _menu.ResetPositionRequested += OnResetPositionRequested;
        _menu.InstallUpdateRequested += () => InstallUpdateRequested?.Invoke();
        _menu.CheckUpdateRequested += () => CheckUpdateRequested?.Invoke();
        _menu.Opened += (_, _) => SetMenuOpen(true);
        _menu.Closed += (_, _) => SetMenuOpen(false);
        // The overlay never activates, so WPF cannot dismiss the menu on a click elsewhere; watch for those clicks.
        _menuOutsideClicks = new OutsideClickWatcher(Dispatcher, MenuScreenBounds, () => _menu.IsOpen = false);

        // The saved dock is aligned first, so the first mode is shown already in its docked form.
        ApplyDock(DockFrom(initial));
        ApplyMode(vm.Mode, instant: true);
        if (vm.Suspended) _transitions.SnapRootOpacity(0.0);
        RootGrid.IsHitTestVisible = !vm.Suspended;

        Reposition(initial);

        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) => Reposition();
        Closed += OnClosed;
        // The overlay never activates, so this is only a fallback that dismisses the menu if activation moves away.
        Deactivated += (_, _) => _menu.IsOpen = false;

        IslandShape.MouseEnter += (_, _) =>
        {
            _pointerInside = true;
            _leaveTimer.Stop();
            UpdateInteraction(_vm.Mode);
        };
        IslandShape.MouseLeave += (_, _) =>
        {
            _pointerInside = false;
            UpdateInteraction(_vm.Mode);
            ScheduleLeaveCollapse();
        };

        _holdTimer = new System.Windows.Threading.DispatcherTimer { Interval = HoldDuration };
        _holdTimer.Tick += OnHoldElapsed;

        _leaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = LeaveCollapseDelay };
        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            bool collapsible = _vm.Mode is IslandMode.Expanded or IslandMode.Clipboard;
            if (collapsible && !_pointerInside && !_menuOpen && !_dragActive && _vm.CollapseCommand.CanExecute(null))
            {
                _vm.CollapseCommand.Execute(null);
            }
        };
        IslandShape.MouseWheel += OnIslandWheel;
        IslandShape.MouseLeftButtonDown += OnIslandPressed;
        IslandShape.MouseMove += OnIslandMouseMove;
        IslandShape.MouseLeave += (_, _) => CheckHoldSlop();
        // The release handler runs first so a drag can swallow the click that ends it.
        IslandShape.MouseLeftButtonUp += OnIslandReleased;
        IslandShape.MouseLeftButtonUp += OnIslandClicked;
        IslandShape.MouseRightButtonUp += OnIslandRightClicked;

        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>"Open Settings" was chosen from the island context menu.</summary>
    public event Action? OpenSettingsRequested;

    /// <summary>"Quit" was chosen from the island context menu.</summary>
    public event Action? QuitRequested;

    /// <summary>"Install update" was chosen from the island context menu (shown only when an update is known).</summary>
    public event Action? InstallUpdateRequested;

    /// <summary>"Check for updates" was chosen in the context menu.</summary>
    public event Action? CheckUpdateRequested;

    /// <summary>Explorer recreated the taskbar (its "TaskbarCreated" broadcast). Raised on the UI thread after the overlay is re-asserted.</summary>
    public event Action? TaskbarRecreated;

    /// <summary>Re-reads settings (monitor, sizes, top margin, reduced motion, shelf widgets) and applies them.</summary>
    public void ApplySettings()
    {
        IslandSettings settings = _settings();
        _transitions.ReduceAnimations = settings.ReduceAnimations;
        _vm.RefreshMotionPreferences();
        ShelfLayer.SetSaved(settings.GetShelfRows());

        SyncDock(settings);
        ApplyShape(_vm.Mode, settings, instant: false);
        Reposition(settings);
    }

    /// <summary>Re-asserts topmost without activating, then repositions. Call on display-settings change.</summary>
    public void Nudge()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) OverlayWindowService.KeepOnTop(hwnd);
        SyncDock(_settings());
        Reposition();
    }

    /// <summary>The dock the saved settings ask for. A dock only exists with a custom position.</summary>
    private static DockEdge DockFrom(IslandSettings settings) =>
        settings.UseCustomPosition ? settings.Dock : DockEdge.None;

    /// <summary>Follows the dock in the settings. A drag owns the dock while it runs; its result is saved on release.</summary>
    private void SyncDock(IslandSettings settings)
    {
        DockEdge dock = DockFrom(settings);
        if (!_dragging && dock != _dock) SetDock(dock, instant: false);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        OverlayWindowService.ApplyOverlayStyles(hwnd);

        _hwndSource = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        Reposition();
        if (_vm.Suspended) Visibility = System.Windows.Visibility.Hidden;
    }

    /// <summary>Never activate on click, so the overlay cannot take focus from the foreground app. Also handles an Explorer restart.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseActivate)
        {
            handled = true;
            return (IntPtr)MaNoActivate;
        }

        // Explorer restarted: the taskbar and topmost ordering were rebuilt, so re-assert the overlay and let the tray refresh.
        uint taskbarCreated = OverlayWindowService.TaskbarCreatedMessage;
        if (taskbarCreated != 0 && (uint)msg == taskbarCreated)
        {
            Serilog.Log.Information("Explorer restarted (TaskbarCreated): re-asserting the overlay and tray icon");
            Nudge();
            TaskbarRecreated?.Invoke();
        }
        return IntPtr.Zero;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IslandViewModel.Mode):
                ApplyMode(_vm.Mode, instant: !IsVisible);
                break;
            case nameof(IslandViewModel.Suspended):
                ApplySuspended(_vm.Suspended);
                break;
            case nameof(IslandViewModel.PomodoroRunning):
                // The compact capsule widens while a pomodoro runs.
                if (_vm.Mode == IslandMode.Compact) ApplyShape(IslandMode.Compact, _settings(), instant: false);
                break;
            case nameof(IslandViewModel.HasPendingTasks):
                // Reserve room for the task indicator in either compact orientation.
                if (_vm.Mode == IslandMode.Compact) ApplyShape(IslandMode.Compact, _settings(), instant: false);
                break;
            case nameof(IslandViewModel.Notice):
                // Notice is bound through a snapshot: update it during the same layer transition, after the old
                // toast has faded out. This keeps back-to-back notices visually legible.
                if (_vm.Mode == IslandMode.Notice)
                    _transitions.ShowLayer(NoticeLayer, instant: false, beforeIn: () => NoticeLayer.SetNotice(_vm.Notice));
                break;
        }
    }

    private void ApplyMode(IslandMode mode, bool instant)
    {
        // Entering edit mode starts from the saved rows; the shape must already see the working copy.
        if (mode == IslandMode.Customize) ShelfLayer.BeginEdit(_settings().GetShelfRows());

        ShowMode(mode, instant);
    }

    /// <summary>Shows a mode's shape and layer in the current dock. Leaves the shelf's working copy alone.</summary>
    private void ShowMode(IslandMode mode, bool instant)
    {
        ApplyShape(mode, _settings(), instant);

        System.Windows.UIElement layer = LayerFor(mode);
        Action? beforeIn = ReferenceEquals(layer, ShelfLayer)
            ? ShelfPresentation(mode == IslandMode.Customize)
            : ReferenceEquals(layer, NoticeLayer)
                ? () => NoticeLayer.SetNotice(_vm.Notice)
                : null;
        _transitions.ShowLayer(layer, instant, beforeIn);

        UpdateInteraction(mode);
    }

    /// <summary>Stores the dock and aligns the island to it: flush to the edge and centred vertically when docked.</summary>
    private void ApplyDock(DockEdge dock)
    {
        _dock = dock;
        IslandShape.HorizontalAlignment = dock switch
        {
            DockEdge.Left => System.Windows.HorizontalAlignment.Left,
            DockEdge.Right => System.Windows.HorizontalAlignment.Right,
            _ => System.Windows.HorizontalAlignment.Center,
        };
        IslandShape.VerticalAlignment = dock == DockEdge.None
            ? System.Windows.VerticalAlignment.Top
            : System.Windows.VerticalAlignment.Center;
    }

    /// <summary>Docks the island to an edge (or None) and shows the current mode in that form.</summary>
    private void SetDock(DockEdge dock, bool instant)
    {
        ApplyDock(dock);
        ShowMode(_vm.Mode, instant);
    }

    /// <summary>Switches the shared shelf between the saved presentation and edit presentation.</summary>
    private Action ShelfPresentation(bool editing) =>
        () => ShelfLayer.SetPresentation(editing, _settings().GetShelfRows());

    /// <summary>The island is as wide as the row on screen (the working copy's row in edit mode).</summary>
    private void ApplyShape(IslandMode mode, IslandSettings settings, bool instant)
    {
        IEnumerable<string> shelfIds = ShelfLayer.PageIds(editing: mode == IslandMode.Customize);
        ShapeSize shape = IslandShapeTable.For(mode, settings, _vm.PomodoroRunning, _vm.HasPendingTasks,
            shelfIds, vertical: _dock != DockEdge.None);
        _transitions.SetShape(shape.Width, shape.Height, shape.Radius, instant);
    }

    /// <summary>A card or tile drag is in progress: keep the island open even if the pointer leaves it.</summary>
    private void OnShelfDragActiveChanged(bool active)
    {
        _dragActive = active;
        UpdateInteraction(_vm.Mode);
    }

    /// <summary>The shelf's row on screen or its rows changed (edit, drag preview, row change): resize the shape (a spring, no content swap).</summary>
    private void OnShelfLayoutChanged()
    {
        if (_vm.Mode is IslandMode.Expanded or IslandMode.Customize) ApplyShape(_vm.Mode, _settings(), instant: false);
    }

    private void ApplySuspended(bool suspended)
    {
        if (suspended)
        {
            RootGrid.IsHitTestVisible = false;
            _transitions.FadeRoot(0.0, () =>
            {
                // Only hide if we are still suspended when the fade completes.
                if (_vm.Suspended) Visibility = System.Windows.Visibility.Hidden;
            });
            return;
        }

        RootGrid.IsHitTestVisible = true;
        if (Visibility != System.Windows.Visibility.Visible) Visibility = System.Windows.Visibility.Visible;
        _transitions.FadeRoot(1.0, null);
    }

    /// <summary>Time of the last wheel page turn, for the debounce.</summary>
    private DateTime _lastWheelPage = DateTime.MinValue;

    /// <summary>
    /// Mouse wheel over the island turns its pages: the shelf rows top to bottom, then the Clipboard. Down is the next
    /// page, up the previous one, with no wrap. From any temporary state it reopens the last panel. Debounced so one wheel
    /// gesture (or trackpad inertia) turns one page.
    /// </summary>
    private void OnIslandWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm.Suspended || e.Delta == 0) return;

        DateTime now = DateTime.UtcNow;
        if ((now - _lastWheelPage).TotalMilliseconds < 400)
        {
            e.Handled = true;
            return;
        }

        bool down = e.Delta < 0;
        switch (_vm.Mode)
        {
            case IslandMode.Compact or IslandMode.Volume or IslandMode.MediaPreview or IslandMode.Notice:
                _vm.ReopenCommand.Execute(null);
                break;
            case IslandMode.Expanded or IslandMode.Customize when ShelfLayer.TryMovePage(down ? 1 : -1):
                break;
            case IslandMode.Expanded when down && _settings().ClipboardEnabled
                                           && ShelfLayer.Page == ShelfLayer.PageCount(editing: false) - 1:
                _vm.OpenClipboardCommand.Execute(null);
                break;
            case IslandMode.Clipboard when !down:
                // Up from the Clipboard lands on the last shelf row.
                ShelfLayer.JumpToPage(ShelfLayer.PageCount(editing: false) - 1);
                _vm.ExpandCommand.Execute(null);
                break;
            default:
                return;
        }

        _lastWheelPage = now;
        e.Handled = true;
    }

    private void OnIslandClicked(object sender, MouseButtonEventArgs e)
    {
        if (_suppressClick)
        {
            _suppressClick = false;
            return;
        }

        // A click on the Mini pill restores Compact; a further click on Compact expands as usual.
        if (_vm.Mode == IslandMode.Mini)
        {
            _vm.CollapseCommand.Execute(null);
            return;
        }

        bool canExpand = _vm.Mode is IslandMode.Compact or IslandMode.Volume
            or IslandMode.MediaPreview or IslandMode.Notice;
        if (canExpand && _vm.ReopenCommand.CanExecute(null))
        {
            _vm.ReopenCommand.Execute(null);
        }
    }

    private void OnIslandRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (_vm.Suspended) return;

        _menu.SetClipboardAvailable(_settings().ClipboardEnabled);
        _menu.SetResetAvailable(_settings().UseCustomPosition);
        _menu.SetMinimizeAvailable(_vm.Mode != IslandMode.Mini);
        _menu.SetUpdateAvailable(_availableUpdate()?.Tag);
        _menu.PlacementTarget = IslandShape;
        _menu.Placement = PlacementMode.MousePoint;
        _menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Screen rectangle (physical pixels) of the open context menu's popup window.</summary>
    private (int Left, int Top, int Right, int Bottom)? MenuScreenBounds()
    {
        if (PresentationSource.FromVisual(_menu) is not HwndSource source) return null;
        if (!GetWindowRect(source.Handle, out RectStruct r)) return null;
        return (r.Left, r.Top, r.Right, r.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectStruct
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RectStruct rect);

    private void SetMenuOpen(bool open)
    {
        _menuOpen = open;
        if (open) _menuOutsideClicks.Start();
        else _menuOutsideClicks.Stop();
        UpdateInteraction(_vm.Mode);
        // Closing the menu with the pointer outside (e.g. by clicking elsewhere) should also close the island.
        if (!open) ScheduleLeaveCollapse();
    }

    private void ScheduleLeaveCollapse()
    {
        if (_vm.Mode is IslandMode.Expanded or IslandMode.Clipboard)
        {
            _leaveTimer.Stop();
            _leaveTimer.Start();
        }
    }

    /// <summary>Holds temporary states open while the pointer is over a shelf-like mode or a menu is open.</summary>
    private void UpdateInteraction(IslandMode mode)
    {
        bool hold = _menuOpen || _dragActive || (_pointerInside && HoldsInteraction(mode));
        if (hold == _interactionSent) return;

        _interactionSent = hold;
        _vm.SetInteracting(hold);
    }

    private static bool HoldsInteraction(IslandMode mode) =>
        mode is IslandMode.Expanded or IslandMode.Customize or IslandMode.Clipboard;

    /// <summary>Places the window at the saved position, or top-centre on the chosen monitor (physical pixels converted to DIPs).</summary>
    private void Reposition() => Reposition(_settings());

    private void Reposition(IslandSettings settings)
    {
        // A drag moves the window itself; a DPI change during it must not snap the window back to the saved spot.
        if (_dragging) return;

        if (settings.UseCustomPosition)
        {
            // Docked: PositionY is the island's centre and PositionX only picks the monitor.
            bool docked = _dock != DockEdge.None;
            if (FindMonitor(settings.PositionX, settings.PositionY, nearest: docked) is { } saved)
            {
                if (docked) PlaceDocked(settings.PositionY, saved, _dock);
                else PlaceIsland(settings.PositionX, settings.PositionY, saved, settings);
                return;
            }
        }

        MonitorInfo monitor = _monitors.GetMonitor(settings.MonitorIndex);
        double scale = monitor.DpiScale > 0.0 ? monitor.DpiScale : 1.0;

        double centreX = (monitor.X + monitor.Width / 2.0) / scale;
        Left = centreX - Width / 2.0;
        Top = monitor.Y / scale + settings.TopMargin;
    }

    /// <summary>
    /// Moves the window so the island's top-centre lands on a physical point, clamped so the compact island stays inside
    /// the monitor. Returns the clamped point. The island grows from its centre, so the current width is used for the clamp.
    /// </summary>
    private (int X, int Y) PlaceIsland(int x, int y, MonitorInfo monitor, IslandSettings settings)
    {
        double scale = monitor.DpiScale > 0.0 ? monitor.DpiScale : 1.0;
        double shapeWidth = Math.Max(IslandShape.ActualWidth, settings.CompactWidth) * scale;
        double shapeHeight = settings.CompactHeight * scale;

        double centreX = ClampRange(x, monitor.X + shapeWidth / 2.0, monitor.X + monitor.Width - shapeWidth / 2.0);
        double top = ClampRange(y, monitor.Y, monitor.Y + monitor.Height - shapeHeight);

        Left = centreX / scale - Width / 2.0;
        Top = top / scale;
        return ((int)Math.Round(centreX), (int)Math.Round(top));
    }

    /// <summary>
    /// Puts the window flush against a monitor edge, with the island centred vertically on a physical Y. The centre is
    /// clamped so the whole window stays on the monitor. Returns the clamped centre.
    /// </summary>
    private int PlaceDocked(int centreY, MonitorInfo monitor, DockEdge dock)
    {
        double scale = monitor.DpiScale > 0.0 ? monitor.DpiScale : 1.0;
        double halfHeight = Height / 2.0 * scale;
        double centre = ClampRange(centreY, monitor.Y + halfHeight, monitor.Y + monitor.Height - halfHeight);

        Left = dock == DockEdge.Right
            ? (monitor.X + monitor.Width) / scale - Width - DockMarginDip
            : monitor.X / scale + DockMarginDip;
        Top = centre / scale - Height / 2.0;
        return (int)Math.Round(centre);
    }

    /// <summary>
    /// The edge a pointer at a physical point docks to, or None. Only the monitor under the pointer counts. An edge is
    /// entered within DockEnterDip and left only beyond DockLeaveDip, so the island does not flicker at the threshold.
    /// </summary>
    private DockEdge DetectDock(double cursorX, double cursorY, MonitorInfo monitor, double scale)
    {
        bool onMonitor = cursorX >= monitor.X && cursorX < monitor.X + monitor.Width
            && cursorY >= monitor.Y && cursorY < monitor.Y + monitor.Height;
        if (!onMonitor) return DockEdge.None;

        double leftLimit = (_dock == DockEdge.Left ? DockLeaveDip : DockEnterDip) * scale;
        double rightLimit = (_dock == DockEdge.Right ? DockLeaveDip : DockEnterDip) * scale;
        if (cursorX - monitor.X < leftLimit) return DockEdge.Left;
        if (monitor.X + monitor.Width - cursorX < rightLimit) return DockEdge.Right;
        return DockEdge.None;
    }

    private static double ClampRange(double value, double min, double max) =>
        max < min ? (min + max) / 2.0 : Math.Clamp(value, min, max);

    /// <summary>The monitor whose rectangle holds a physical point, or with nearest the closest one when none does.</summary>
    private MonitorInfo? FindMonitor(int x, int y, bool nearest)
    {
        MonitorInfo? closest = null;
        long closestDistance = long.MaxValue;
        foreach (MonitorInfo m in _monitors.GetMonitors())
        {
            if (x >= m.X && x < m.X + m.Width && y >= m.Y && y < m.Y + m.Height) return m;
            if (!nearest) continue;

            long dx = Math.Max(0, Math.Max(m.X - x, x - (m.X + m.Width - 1)));
            long dy = Math.Max(0, Math.Max(m.Y - y, y - (m.Y + m.Height - 1)));
            long distance = dx * dx + dy * dy;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = m;
            }
        }
        return closest;
    }

    /// <summary>Pointer position in physical screen pixels, the same space as the saved position.</summary>
    private (double X, double Y) CursorScreen()
    {
        System.Windows.Point p = PointToScreen(Mouse.GetPosition(this));
        return (p.X, p.Y);
    }

    /// <summary>Only states without interactive content can be dragged; Expanded, Customize and Clipboard hold controls.</summary>
    private static bool CanDrag(IslandMode mode) =>
        mode is IslandMode.Compact or IslandMode.Volume or IslandMode.MediaPreview or IslandMode.Notice or IslandMode.Mini;

    /// <summary>A press on the island starts the hold timer. Pressing again clears a click swallowed by an earlier drag.</summary>
    private void OnIslandPressed(object sender, MouseButtonEventArgs e)
    {
        _suppressClick = false;
        if (_vm.Suspended || !CanDrag(_vm.Mode)) return;

        (_pressX, _pressY) = CursorScreen();
        _holdPending = true;
        _holdTimer.Start();
    }

    private void OnIslandMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_holdPending)
        {
            CheckHoldSlop();
            return;
        }
        if (!_dragging) return;

        (double cursorX, double cursorY) = CursorScreen();
        RecordDragSample(cursorX, cursorY);
        MonitorInfo pointerMonitor = FindMonitor((int)Math.Round(cursorX), (int)Math.Round(cursorY), nearest: true)
            ?? _monitors.GetMonitor(0);
        double scale = pointerMonitor.DpiScale > 0.0 ? pointerMonitor.DpiScale : 1.0;

        DockEdge dock = DetectDock(cursorX, cursorY, pointerMonitor, scale);
        if (dock != _dock)
        {
            // Docking or undocking moves the island's centre to the pointer, so it does not jump away from it.
            _grabX = 0.0;
            _grabY = IslandShapeTable.For(_vm.Mode, _settings(), _vm.PomodoroRunning, _vm.HasPendingTasks, Array.Empty<string>(),
                vertical: dock != DockEdge.None).Height / 2.0 * scale;
            SetDock(dock, instant: false);
        }

        if (_dock != DockEdge.None)
        {
            // Docked: the window stays on its edge and only the centre follows the pointer's Y.
            _dragX = (int)Math.Round(ClampRange(cursorX, pointerMonitor.X, pointerMonitor.X + pointerMonitor.Width - 1.0));
            _dragY = PlaceDocked((int)Math.Round(cursorY), pointerMonitor, _dock);
        }
        else
        {
            // Keep the pointer at the same spot on the island: the top-centre follows the pointer minus the grab offset.
            int x = (int)Math.Round(cursorX - _grabX);
            int y = (int)Math.Round(cursorY - _grabY);
            MonitorInfo monitor = FindMonitor(x, y, nearest: true) ?? _monitors.GetMonitor(0);
            (_dragX, _dragY) = PlaceIsland(x, y, monitor, _settings());
        }
        _dragMoved = true;
    }

    /// <summary>Cancels a pending hold once the pointer has moved more than HoldSlop from the press point.</summary>
    private void CheckHoldSlop()
    {
        if (!_holdPending) return;

        (double x, double y) = CursorScreen();
        double dx = x - _pressX;
        double dy = y - _pressY;
        if (Math.Sqrt(dx * dx + dy * dy) > HoldSlop) CancelHold();
    }

    private void CancelHold()
    {
        _holdPending = false;
        _holdTimer.Stop();
    }

    /// <summary>The press was held still long enough: the island now follows the pointer.</summary>
    private void OnHoldElapsed(object? sender, EventArgs e)
    {
        _holdTimer.Stop();
        CheckHoldSlop();
        if (!_holdPending) return;

        // The button must still be down, the mode must still allow dragging, and the press must not have been cancelled.
        if (_vm.Suspended || !CanDrag(_vm.Mode) || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            CancelHold();
            return;
        }

        _holdPending = false;
        BeginDrag();
    }

    private void BeginDrag()
    {
        _dragging = true;
        _dragMoved = false;

        // The island's top-centre in physical pixels: the pointer keeps its offset from it for the whole drag.
        System.Windows.Point top = IslandShape.PointToScreen(new System.Windows.Point(IslandShape.ActualWidth / 2.0, 0.0));
        (double cursorX, double cursorY) = CursorScreen();
        _grabX = cursorX - top.X;
        _grabY = cursorY - top.Y;
        _dragX = (int)Math.Round(top.X);
        _dragY = (int)Math.Round(top.Y);

        _dragSamples.Clear();
        RecordDragSample(cursorX, cursorY);

        IslandShape.CaptureMouse();
        IslandShape.Opacity = 0.85;
        SetDragHeld(true);
    }

    /// <summary>A drag holds the island in its state like a shelf drag does, so it does not change mode under the pointer.</summary>
    private void SetDragHeld(bool held)
    {
        _dragActive = held;
        UpdateInteraction(_vm.Mode);
    }

    /// <summary>Ends a hold or drag. A drag that moved saves its position; the click that ends a drag is swallowed.</summary>
    private void OnIslandReleased(object sender, MouseButtonEventArgs e)
    {
        CancelHold();
        if (!_dragging) return;

        _dragging = false;
        IslandShape.ReleaseMouseCapture();
        IslandShape.Opacity = 1.0;
        SetDragHeld(false);
        _suppressClick = true;

        if (_dragMoved)
        {
            _saveSettings?.Invoke(_settings() with
            {
                UseCustomPosition = true,
                Dock = _dock,
                PositionX = _dragX,
                PositionY = _dragY,
            });

            // A throw upward minimises the island, after its position is saved. Already Mini: nothing to do.
            (double releaseX, double releaseY) = CursorScreen();
            RecordDragSample(releaseX, releaseY);
            MonitorInfo releaseMonitor = FindMonitor((int)Math.Round(releaseX), (int)Math.Round(releaseY), nearest: true)
                ?? _monitors.GetMonitor(0);
            double releaseScale = releaseMonitor.DpiScale > 0.0 ? releaseMonitor.DpiScale : 1.0;
            if (_vm.Mode != IslandMode.Mini && IsThrowUp(releaseScale))
            {
                _vm.MinimizeCommand.Execute(null);
            }
        }
        _dragMoved = false;
        _dragSamples.Clear();
    }

    /// <summary>Keeps the last <see cref="ThrowBufferMs"/> of a drag's pointer positions.</summary>
    private void RecordDragSample(double cursorX, double cursorY)
    {
        long now = Environment.TickCount64;
        _dragSamples.RemoveAll(s => now - s.Time > ThrowBufferMs);
        _dragSamples.Add((now, cursorX, cursorY));
    }

    /// <summary>
    /// True when the pointer rose fast over the last <see cref="ThrowWindowMs"/>: at least <see cref="ThrowSpeedDip"/> DIP/s
    /// upward and more vertical than horizontal. Needs samples spanning at least <see cref="ThrowMinSpanMs"/>.
    /// </summary>
    private bool IsThrowUp(double scale)
    {
        long now = Environment.TickCount64;
        var recent = _dragSamples.Where(s => now - s.Time <= ThrowWindowMs).ToList();
        if (recent.Count < 2) return false;

        (long t0, double x0, double y0) = recent[0];
        (long t1, double x1, double y1) = recent[^1];
        long spanMs = t1 - t0;
        if (spanMs < ThrowMinSpanMs) return false;

        // Samples are physical pixels per second; the threshold scales with the monitor's DPI. Negative vy is upward.
        double seconds = spanMs / 1000.0;
        double vx = (x1 - x0) / seconds;
        double vy = (y1 - y0) / seconds;
        return vy <= -ThrowSpeedDip * scale && Math.Abs(vy) >= 2.0 * Math.Abs(vx);
    }

    /// <summary>"Reset position" was chosen: the island returns to the top-centre of the chosen monitor.</summary>
    private void OnResetPositionRequested() =>
        _saveSettings?.Invoke(_settings() with { UseCustomPosition = false, Dock = DockEdge.None });

    private System.Windows.UIElement LayerFor(IslandMode mode)
    {
        // A docked island shows its vertical capsule and volume bar.
        bool vertical = _dock != DockEdge.None;
        return mode switch
        {
            IslandMode.Volume => vertical ? VerticalVolumeLayer : VolumeLayer,
            IslandMode.MediaPreview => PreviewLayer,
            IslandMode.Notice => NoticeLayer,
            IslandMode.Expanded => ShelfLayer,
            IslandMode.Customize => ShelfLayer,
            IslandMode.Clipboard => ClipboardLayer,
            // The Mini pill has one form, docked or not.
            IslandMode.Mini => MiniLayer,
            _ => vertical ? VerticalCompactLayer : CompactLayer,
        };
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        ShelfLayer.LayoutChanged -= OnShelfLayoutChanged;
        ShelfLayer.DragActiveChanged -= OnShelfDragActiveChanged;
        _menu.IsOpen = false;
        _menuOutsideClicks.Dispose();
        _leaveTimer.Stop();
        CancelHold();
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
    }
}
