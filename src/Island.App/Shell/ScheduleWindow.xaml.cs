using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Island.Core.Pomodoro;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace Island.App.Shell;

/// <summary>
/// "Agendar pomodoro" dialog: a clock time (HH:mm), a mode (Focus or Angry) and a count of pomodoros in a row. The
/// confirm button schedules the next occurrence of that time through <see cref="PomodoroSchedule.Set"/>; the remove
/// button appears only while a start is pending. Esc closes without changes. Open it with <see cref="ShowFor"/>.
/// </summary>
public partial class ScheduleWindow : Window
{
    private static ScheduleWindow? _open;

    private static readonly Brush Orange = Frozen(0xFF, 0x9F, 0x0A, 0xFF);
    private static readonly Brush OrangeSoft = Frozen(0xFF, 0x9F, 0x0A, 0x33);
    private static readonly Brush Red = Frozen(0xFF, 0x45, 0x3A, 0xFF);
    private static readonly Brush RedSoft = Frozen(0xFF, 0x45, 0x3A, 0x33);
    private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

    private readonly PomodoroSchedule _schedule;
    private ScheduledStartMode _mode;
    private int _cycles;

    public ScheduleWindow(PomodoroSchedule schedule, int defaultCycles)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        InitializeComponent();
        _schedule = schedule;

        // A pending start pre-fills the dialog; otherwise the next five-minute mark after now.
        ScheduledStart? pending = schedule.Pending;
        _mode = pending?.Mode ?? ScheduledStartMode.Focus;
        _cycles = Math.Clamp(pending?.Cycles ?? defaultCycles, PomodoroTimer.MinCycles, PomodoroTimer.MaxCycles);
        TimeBox.Text = pending is not null
            ? pending.At.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
            : NextFiveMinutes(DateTime.Now).ToString("HH:mm", CultureInfo.InvariantCulture);
        RemoveButton.Visibility = pending is null ? Visibility.Collapsed : Visibility.Visible;

        ApplyMode();
        ApplyCycles();
        UpdateTime();

        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
            }
        };
    }

    /// <summary>
    /// Shows the dialog modally. Only one is open at a time: when it is already open it is activated instead. Nothing
    /// is changed unless the user confirms or removes the start.
    /// </summary>
    public static void ShowFor(PomodoroSchedule schedule, int defaultCycles)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (_open is not null)
        {
            _open.Activate();
            return;
        }

        var window = new ScheduleWindow(schedule, defaultCycles);
        _open = window;
        try
        {
            window.ShowDialog();
        }
        finally
        {
            _open = null;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Dark title bar, like the other app windows (DWMWA_USE_IMMERSIVE_DARK_MODE = 20, Windows 10 2004+).
        int useDark = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref useDark, sizeof(int));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        TimeBox.Focus();
        TimeBox.SelectAll();
    }

    /// <summary>The first multiple of five minutes strictly after <paramref name="now"/>, wrapping past midnight.</summary>
    private static TimeOnly NextFiveMinutes(DateTime now)
    {
        int minutes = (now.Hour * 60 + now.Minute) / 5 * 5 + 5;
        minutes %= 24 * 60;
        return new TimeOnly(minutes / 60, minutes % 60);
    }

    private void OnTimeChanged(object sender, TextChangedEventArgs e) => UpdateTime();

    /// <summary>Validates the time field, then shows the preview, or the error and a disabled confirm button.</summary>
    private void UpdateTime()
    {
        bool valid = TryReadTime(out TimeOnly time);
        TimeError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.IsEnabled = valid;

        if (!valid)
        {
            PreviewText.Text = string.Empty;
            return;
        }

        DateTimeOffset at = PomodoroSchedule.NextOccurrence(time, DateTimeOffset.Now);
        string day = at.ToLocalTime().Date == DateTime.Now.Date ? "hoje" : "amanhã";
        PreviewText.Text = $"Começa {day} às {time.ToString("HH:mm", CultureInfo.InvariantCulture)}";
    }

    private bool TryReadTime(out TimeOnly time) =>
        TimeOnly.TryParseExact(TimeBox.Text.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private void OnFocusModeClicked(object sender, RoutedEventArgs e) => SetMode(ScheduledStartMode.Focus);

    private void OnAngryModeClicked(object sender, RoutedEventArgs e) => SetMode(ScheduledStartMode.Angry);

    private void SetMode(ScheduledStartMode mode)
    {
        _mode = mode;
        ApplyMode();
    }

    /// <summary>Selected pill: tinted background and accent label (orange for Focus, red for Angry).</summary>
    private void ApplyMode()
    {
        bool angry = _mode == ScheduledStartMode.Angry;
        StylePill(FocusModeButton, selected: !angry, OrangeSoft, Orange);
        StylePill(AngryModeButton, selected: angry, RedSoft, Red);
    }

    private void StylePill(System.Windows.Controls.Button button, bool selected, Brush soft, Brush accent)
    {
        button.Background = selected ? soft : (Brush)FindResource("ShelfButtonBrush");
        button.Foreground = selected ? accent : (Brush)FindResource("TextSecondaryBrush");
    }

    private void OnMinusClicked(object sender, RoutedEventArgs e)
    {
        _cycles = Math.Max(PomodoroTimer.MinCycles, _cycles - 1);
        ApplyCycles();
    }

    private void OnPlusClicked(object sender, RoutedEventArgs e)
    {
        _cycles = Math.Min(PomodoroTimer.MaxCycles, _cycles + 1);
        ApplyCycles();
    }

    private void ApplyCycles()
    {
        CountText.Text = _cycles.ToString(CultureInfo.InvariantCulture);
        MinusButton.IsEnabled = _cycles > PomodoroTimer.MinCycles;
        PlusButton.IsEnabled = _cycles < PomodoroTimer.MaxCycles;
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        _schedule.Cancel();
        DialogResult = false;
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        if (!TryReadTime(out TimeOnly time))
        {
            UpdateTime();
            return;
        }

        _schedule.Set(PomodoroSchedule.NextOccurrence(time, DateTimeOffset.Now), _mode, _cycles);
        DialogResult = true;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static Brush Frozen(byte r, byte g, byte b, byte a)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
