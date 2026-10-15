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
/// "Agendar pomodoro" manager: a day (today to 30 days ahead), a clock time (HH:mm), a mode (Focus or Angry) and a count
/// of pomodoros in a row. "Agendar" adds a start through <see cref="PomodoroSchedule.Add"/> and keeps the form open for
/// another one. The list below shows every scheduled start with a delete button and follows
/// <see cref="PomodoroSchedule.Changed"/> while the dialog is open. Esc or "Fechar" closes. Open it with <see cref="ShowFor"/>.
/// </summary>
public partial class ScheduleWindow : Window
{
    private const int MaxDaysAhead = 30;

    private static ScheduleWindow? _open;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Brush Orange = Frozen(0xFF, 0x9F, 0x0A, 0xFF);
    private static readonly Brush OrangeSoft = Frozen(0xFF, 0x9F, 0x0A, 0x33);
    private static readonly Brush Red = Frozen(0xFF, 0x45, 0x3A, 0xFF);
    private static readonly Brush RedSoft = Frozen(0xFF, 0x45, 0x3A, 0x33);
    private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

    private readonly PomodoroSchedule _schedule;
    private ScheduledStartMode _mode = ScheduledStartMode.Focus;
    private int _cycles;
    private int _dayOffset;

    public ScheduleWindow(PomodoroSchedule schedule, int defaultCycles)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        InitializeComponent();
        _schedule = schedule;

        // Form defaults: today, the next five-minute mark after now, Focus, and the configured cycles.
        _cycles = Math.Clamp(defaultCycles, PomodoroTimer.MinCycles, PomodoroTimer.MaxCycles);
        TimeBox.Text = NextFiveMinutes(DateTime.Now).ToString("HH:mm", CultureInfo.InvariantCulture);

        ApplyDay();
        ApplyMode();
        ApplyCycles();
        RefreshStarts();
        UpdateForm();

        _schedule.Changed += OnScheduleChanged;
    }

    /// <summary>
    /// Shows the dialog modally. Only one is open at a time: when it is already open it is activated instead. Nothing
    /// is changed unless the user adds or deletes a start.
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
        Island.Windows.Display.WindowAppearance.EnableDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _schedule.Changed -= OnScheduleChanged;
        base.OnClosed(e);
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

    /// <summary>"Hoje", "Amanhã", or the short weekday and day, like "qua, 15/10", for any other date.</summary>
    private static string DayLabel(DateOnly date)
    {
        int offset = date.DayNumber - DateOnly.FromDateTime(DateTime.Now).DayNumber;
        if (offset == 0)
        {
            return "Hoje";
        }

        if (offset == 1)
        {
            return "Amanhã";
        }

        string weekday = Pt.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek).TrimEnd('.');
        return $"{weekday}, {date.ToString("dd/MM", CultureInfo.InvariantCulture)}";
    }

    private DateOnly SelectedDate => DateOnly.FromDateTime(DateTime.Now).AddDays(_dayOffset);

    private void OnDayBackClicked(object sender, RoutedEventArgs e)
    {
        _dayOffset = Math.Max(0, _dayOffset - 1);
        ApplyDay();
        UpdateForm();
    }

    private void OnDayForwardClicked(object sender, RoutedEventArgs e)
    {
        _dayOffset = Math.Min(MaxDaysAhead, _dayOffset + 1);
        ApplyDay();
        UpdateForm();
    }

    private void ApplyDay()
    {
        DayText.Text = DayLabel(SelectedDate);
        DayBackButton.IsEnabled = _dayOffset > 0;
        DayForwardButton.IsEnabled = _dayOffset < MaxDaysAhead;
    }

    private void OnTimeChanged(object sender, TextChangedEventArgs e) => UpdateForm();

    /// <summary>
    /// Validates the time against the chosen day, then shows the preview, or the error and a disabled confirm button.
    /// </summary>
    private void UpdateForm()
    {
        bool validFormat = TryReadTime(out TimeOnly time);
        DateTimeOffset now = DateTimeOffset.Now;
        bool past = validFormat && PomodoroSchedule.At(SelectedDate, time, now) <= now;
        bool valid = validFormat && !past;

        string? error = !validFormat ? "Use o formato HH:mm, por exemplo 14:30." : past ? "Esse horário já passou" : null;
        TimeError.Text = error ?? string.Empty;
        TimeError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.IsEnabled = valid;

        if (!valid)
        {
            PreviewText.Text = string.Empty;
            return;
        }

        string when = _dayOffset switch
        {
            0 => "hoje",
            1 => "amanhã",
            _ => $"em {DayLabel(SelectedDate)}",
        };
        PreviewText.Text = $"Começa {when} às {time.ToString("HH:mm", CultureInfo.InvariantCulture)}";
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

    private void OnScheduleChanged() => Dispatcher.BeginInvoke(new Action(RefreshStarts));

    /// <summary>Rebuilds the list from <see cref="PomodoroSchedule.Starts"/> (already sorted by time).</summary>
    private void RefreshStarts()
    {
        var rows = new List<StartRow>();
        foreach (ScheduledStart start in _schedule.Starts)
        {
            DateTime local = start.At.ToLocalTime().DateTime;
            bool angry = start.Mode == ScheduledStartMode.Angry;
            rows.Add(new StartRow(
                start.Id,
                DayLabel(DateOnly.FromDateTime(local)),
                local.ToString("HH:mm", CultureInfo.InvariantCulture),
                angry ? "Angry" : "Focus",
                angry ? Red : Orange,
                start.Cycles == 1 ? "1 pomodoro" : $"{start.Cycles} pomodoros"));
        }

        StartsList.ItemsSource = rows;
        bool any = rows.Count > 0;
        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        StartsScroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).Tag is Guid id)
        {
            _schedule.Remove(id);
            RefreshStarts();
        }
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        if (!TryReadTime(out TimeOnly time))
        {
            UpdateForm();
            return;
        }

        _schedule.Add(PomodoroSchedule.At(SelectedDate, time, DateTimeOffset.Now), _mode, _cycles);
        RefreshStarts();
        UpdateForm();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();


    private static Brush Frozen(byte r, byte g, byte b, byte a)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>One row of the scheduled list, as bound by the item template.</summary>
    internal sealed record StartRow(Guid Id, string Day, string Time, string Mode, Brush ModeBrush, string Count);
}
