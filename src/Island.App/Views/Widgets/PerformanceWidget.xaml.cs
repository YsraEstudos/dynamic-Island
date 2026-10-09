using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Island.App.Widgets;
using Island.Core.Performance;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views.Widgets;

/// <summary>
/// Shelf card: live CPU and GPU load and temperature, RAM and VRAM, a sparkline of the last minute, and the temperature
/// alert switch with its two limits (buttons move 1 °C; the wheel moves 1 °C per notch, 5 °C with Shift).
/// Sampling runs only while the card is on screen, see <see cref="PerformanceMonitor"/>.
/// </summary>
public partial class PerformanceWidget : UserControl
{
    private const double BytesPerGigabyte = 1024.0 * 1024.0 * 1024.0;
    private const int WheelCoarseStep = 5;
    private static readonly Brush AlertOnBackground = FrozenColor(0x1F, 0x3A, 0x2A);
    private static readonly Brush AlertOnLabel = FrozenColor(0x30, 0xD1, 0x58);
    private static readonly Brush AlertOffBackground = FrozenColor(0x2C, 0x2C, 0x2E);
    private static readonly Brush AlertOffLabel = FrozenColor(0x6E, 0x6E, 0x73);
    private static readonly string TemperatureUnavailableTip =
        "Temperatura indisponível: este PC não expõe o sensor sem privilégios de administrador.";

    private readonly ShelfContext _context;
    private readonly PerformanceMonitor _monitor;
    private readonly UiSignal _signal;
    private bool _subscribed;

    public PerformanceWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _context = context;
        _monitor = context.Performance ?? throw new InvalidOperationException("Performance monitor is not available.");
        _signal = new UiSignal(Dispatcher, Refresh);

        CpuPanel.Title = "CPU";
        GpuPanel.Title = "GPU";

        AlertToggle.Click += () => ChangeAlerts(a => a with { Enabled = !a.Enabled });
        CpuDown.Click += () => ChangeAlerts(a => a.WithCpuLimit(a.CpuLimitC - 1));
        CpuUp.Click += () => ChangeAlerts(a => a.WithCpuLimit(a.CpuLimitC + 1));
        GpuDown.Click += () => ChangeAlerts(a => a.WithGpuLimit(a.GpuLimitC - 1));
        GpuUp.Click += () => ChangeAlerts(a => a.WithGpuLimit(a.GpuLimitC + 1));
        CpuLimit.MouseWheel += (_, e) => { ChangeAlerts(a => a.WithCpuLimit(a.CpuLimitC + WheelStep(e))); e.Handled = true; };
        GpuLimit.MouseWheel += (_, e) => { ChangeAlerts(a => a.WithGpuLimit(a.GpuLimitC + WheelStep(e))); e.Handled = true; };

        // Visible on screen means sampling; hidden (the shelf is closed, or another row is shown) stops it.
        IsVisibleChanged += (_, e) => _monitor.SetWidgetVisible((bool)e.NewValue);
        Loaded += (_, _) =>
        {
            Subscribe();
            _monitor.SetWidgetVisible(IsVisible);
        };
        Unloaded += (_, _) =>
        {
            Unsubscribe();
            _monitor.SetWidgetVisible(false);
        };
    }

    private void Subscribe()
    {
        if (_subscribed) return;

        _monitor.SnapshotChanged += OnSnapshotChanged;
        _subscribed = true;
        Refresh();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        _monitor.SnapshotChanged -= OnSnapshotChanged;
        _subscribed = false;
    }

    /// <summary>Runs on the sampling thread: only signals; the UI reads the latest snapshot itself.</summary>
    private void OnSnapshotChanged(PerformanceSnapshot _) => _signal.Signal();

    private void Refresh()
    {
        PerformanceAlertSettings alerts = _monitor.Alerts;
        PerformanceSnapshot? snapshot = _monitor.Latest;
        bool reduce = _context.Settings().ReduceAnimations;

        CpuPanel.SetPercent(snapshot?.CpuPercent, reduce);
        CpuPanel.SetTemperature(snapshot?.CpuTempC, alerts.CpuLimitC, TemperatureUnavailableTip);
        CpuPanel.SetDetail(MemoryText(snapshot));
        CpuPanel.SetHistory(_monitor.CpuHistory());

        GpuPanel.SetPercent(snapshot?.GpuPercent, reduce);
        GpuPanel.SetTemperature(snapshot?.GpuTempC, alerts.GpuLimitC, TemperatureUnavailableTip);
        GpuPanel.SetDetail(VramText(snapshot));
        GpuPanel.SetHistory(_monitor.GpuHistory());

        RefreshAlertControls(alerts);
    }

    private void RefreshAlertControls(PerformanceAlertSettings alerts)
    {
        AlertToggle.Background = alerts.Enabled ? AlertOnBackground : AlertOffBackground;
        AlertToggle.LabelBrush = alerts.Enabled ? AlertOnLabel : AlertOffLabel;

        CpuLimitText.Text = $"{alerts.CpuLimitC} °C";
        GpuLimitText.Text = $"{alerts.GpuLimitC} °C";
        CpuLimit.Opacity = alerts.Enabled ? 1.0 : 0.45;
        GpuLimit.Opacity = alerts.Enabled ? 1.0 : 0.45;
    }

    /// <summary>Applies a change to the alert settings, saves it and shows it at once.</summary>
    private void ChangeAlerts(Func<PerformanceAlertSettings, PerformanceAlertSettings> change)
    {
        _monitor.SetAlerts(change(_monitor.Alerts));
        RefreshAlertControls(_monitor.Alerts);
    }

    private static int WheelStep(MouseWheelEventArgs e)
    {
        int direction = Math.Sign(e.Delta);
        bool coarse = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        return direction * (coarse ? WheelCoarseStep : 1);
    }

    private static string MemoryText(PerformanceSnapshot? snapshot)
    {
        if (snapshot?.RamUsedBytes is not { } used || snapshot.RamTotalBytes is not { } total || total == 0)
            return "RAM —";
        return $"RAM {used / BytesPerGigabyte:0.0}/{total / BytesPerGigabyte:0} GB";
    }

    private static string VramText(PerformanceSnapshot? snapshot)
    {
        if (snapshot?.VramUsedBytes is not { } used) return string.Empty;
        if (snapshot.VramTotalBytes is { } total && total > 0)
            return $"VRAM {used / BytesPerGigabyte:0.0}/{total / BytesPerGigabyte:0} GB";
        return $"VRAM {used / BytesPerGigabyte:0.0} GB";
    }

    private static Brush FrozenColor(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
