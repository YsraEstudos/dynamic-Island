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
/// "Agendar pomodoro" manager: a day (today to 30 days ahead), a start and an end time (HH:mm), a mode (Focus or Angry),
/// and the focus and break lengths in minutes. The plan runs as many focus/break pairs as fit between the two times.
/// "Agendar" adds a start and keeps the form open for another one; "Editar" on a row loads it into the form and "Salvar"
/// changes it. The list below shows every scheduled start with edit and delete buttons and follows
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
    private readonly PomodoroWeeklySchedule? _weeklySchedule;
    private ScheduledStartMode _mode = ScheduledStartMode.Focus;
    private int _dayOffset;
    // The start being edited, or null when the form adds a new one.
    private Guid? _editingId;

    public ScheduleWindow(PomodoroSchedule schedule, int defaultFocusMinutes, int defaultBreakMinutes,
        PomodoroWeeklySchedule? weeklySchedule = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        InitializeComponent();
        _schedule = schedule;
        _weeklySchedule = weeklySchedule;
        WeeklyButton.Visibility = weeklySchedule is null ? Visibility.Collapsed : Visibility.Visible;

        // Form defaults: today, the next five-minute mark after now, Focus, and one pomodoro of the configured lengths.
        int focus = Math.Clamp(defaultFocusMinutes, PomodoroTimer.MinMinutes, PomodoroTimer.MaxMinutes);
        int brk = Math.Clamp(defaultBreakMinutes, PomodoroTimer.MinMinutes, PomodoroTimer.MaxMinutes);
        TimeOnly start = NextFiveMinutes(DateTime.Now);
        FocusBox.Text = focus.ToString(CultureInfo.InvariantCulture);
        BreakBox.Text = brk.ToString(CultureInfo.InvariantCulture);
        TimeBox.Text = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        EndBox.Text = start.AddMinutes(focus + brk).ToString("HH:mm", CultureInfo.InvariantCulture);

        ApplyDay();
        ApplyMode();
        ApplyEditing();
        RefreshStarts();
        UpdateForm();

        _schedule.Changed += OnScheduleChanged;
    }

    /// <summary>
    /// Shows the dialog modally. Only one is open at a time: when it is already open it is activated instead. Nothing
    /// is changed unless the user adds, edits or deletes a start.
    /// </summary>
    public static void ShowFor(PomodoroSchedule schedule, int defaultFocusMinutes, int defaultBreakMinutes,
        PomodoroWeeklySchedule? weeklySchedule = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (_open is not null)
        {
            _open.Activate();
            return;
        }

        var window = new ScheduleWindow(schedule, defaultFocusMinutes, defaultBreakMinutes, weeklySchedule);
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

    private static string CountText(int cycles) => cycles == 1 ? "1 pomodoro" : $"{cycles} pomodoros";

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

    private void OnFormChanged(object sender, TextChangedEventArgs e) => UpdateForm();

    /// <summary>
    /// Validates the form, then shows the preview, or the error and a disabled confirm button.
    /// </summary>
    private void UpdateForm()
    {
        string? error = ReadForm(out FormValues values);
        TimeError.Text = error ?? string.Empty;
        TimeError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.IsEnabled = error is null;

        if (error is not null)
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
        PreviewText.Text = $"Começa {when} às {Hm(values.Start)} e termina às {Hm(values.PlannedEnd)} · {CountText(values.Cycles)}";
    }

    /// <summary>
    /// The form as entered, or the reason it cannot be scheduled. <see cref="FormValues.PlannedEnd"/> is when the last
    /// break really ends: the pomodoros are whole pairs, so it can come before the end time typed in.
    /// </summary>
    private string? ReadForm(out FormValues values)
    {
        values = default;
        if (!TryReadTime(TimeBox.Text, out TimeOnly time) || !TryReadTime(EndBox.Text, out TimeOnly endTime))
            return "Use o formato HH:mm, por exemplo 14:30.";
        if (!TryReadMinutes(FocusBox.Text, out int focus) || !TryReadMinutes(BreakBox.Text, out int brk))
            return $"Foco e pausa precisam ter entre {PomodoroTimer.MinMinutes} e {PomodoroTimer.MaxMinutes} minutos.";

        DateTimeOffset now = DateTimeOffset.Now;
        DateTimeOffset start = PomodoroSchedule.At(SelectedDate, time, now);
        DateTimeOffset end = PomodoroSchedule.At(SelectedDate, endTime, now);
        if (start <= now) return "Esse horário já passou";
        if (end <= start) return "O término precisa ser depois do início.";

        int cycles = PomodoroTimer.CyclesFor(end - start, focus, brk);
        if (cycles < 1) return $"Não cabe um pomodoro de {focus} min de foco e {brk} min de pausa nesse intervalo.";

        values = new FormValues(start, end, focus, brk, cycles, start + TimeSpan.FromMinutes(cycles * (focus + brk)));
        return null;
    }

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

    /// <summary>The confirm button adds a start, or saves the one being edited; Cancel edit appears only then.</summary>
    private void ApplyEditing()
    {
        bool editing = _editingId is not null;
        ConfirmButton.Content = editing ? "Salvar" : "Agendar";
        CancelEditButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnScheduleChanged() => Dispatcher.BeginInvoke(new Action(RefreshStarts));

    /// <summary>Rebuilds the list from <see cref="PomodoroSchedule.Starts"/> (already sorted by time).</summary>
    private void RefreshStarts()
    {
        var rows = new List<StartRow>();
        foreach (ScheduledStart start in _schedule.Starts)
        {
            DateTimeOffset local = start.At.ToLocalTime();
            bool angry = start.Mode == ScheduledStartMode.Angry;
            rows.Add(new StartRow(
                start.Id,
                DayLabel(DateOnly.FromDateTime(local.DateTime)),
                $"{Hm(local)}–{Hm(start.End.ToLocalTime())}",
                angry ? "Angry" : "Focus",
                angry ? Red : Orange,
                $"{CountText(start.Cycles)} · {start.FocusMinutes}/{start.BreakMinutes} min"));
        }

        StartsList.ItemsSource = rows;
        bool any = rows.Count > 0;
        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        StartsScroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Loads a scheduled start into the form so that "Salvar" changes it.</summary>
    private void OnEditClicked(object sender, RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).Tag is not Guid id) return;
        ScheduledStart? start = _schedule.Starts.FirstOrDefault(s => s.Id == id);
        if (start is null) return;

        DateTimeOffset local = start.At.ToLocalTime();
        _dayOffset = Math.Clamp(DateOnly.FromDateTime(local.DateTime).DayNumber - DateOnly.FromDateTime(DateTime.Now).DayNumber,
            0, MaxDaysAhead);
        _editingId = id;
        SetMode(start.Mode);
        TimeBox.Text = Hm(local);
        EndBox.Text = Hm(start.End.ToLocalTime());
        FocusBox.Text = start.FocusMinutes.ToString(CultureInfo.InvariantCulture);
        BreakBox.Text = start.BreakMinutes.ToString(CultureInfo.InvariantCulture);
        ApplyDay();
        ApplyEditing();
        UpdateForm();
    }

    private void OnCancelEditClicked(object sender, RoutedEventArgs e)
    {
        _editingId = null;
        ApplyEditing();
        UpdateForm();
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).Tag is not Guid id) return;

        // Deleting the start being edited leaves the form in add mode.
        if (_editingId == id)
        {
            _editingId = null;
            ApplyEditing();
        }
        _schedule.Remove(id);
        RefreshStarts();
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        if (ReadForm(out FormValues values) is not null)
        {
            UpdateForm();
            return;
        }

        bool saved = _editingId is { } id
            ? _schedule.Update(id, values.Start, values.End, _mode, values.Focus, values.Break)
            : _schedule.Add(values.Start, values.End, _mode, values.Focus, values.Break) is not null;
        if (!saved)
        {
            // Only an edit can fail here: the start was removed elsewhere, or its time has passed meanwhile.
            TimeError.Text = "Não foi possível salvar: a faixa não existe mais ou o horário já passou.";
            TimeError.Visibility = Visibility.Visible;
            return;
        }

        if (_editingId is not null)
        {
            _editingId = null;
            ApplyEditing();
        }
        RefreshStarts();
        UpdateForm();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnWeeklyClicked(object sender, RoutedEventArgs e)
    {
        if (_weeklySchedule is not null)
            WeeklyPomodoroScheduleWindow.ShowFor(_weeklySchedule, FocusBoxValue(), BreakBoxValue());
    }

    private int FocusBoxValue() => TryReadMinutes(FocusBox.Text, out int focus) ? focus : PomodoroTimer.DefaultFocusMinutes;

    private int BreakBoxValue() => TryReadMinutes(BreakBox.Text, out int brk) ? brk : PomodoroTimer.DefaultBreakMinutes;

    private static bool TryReadTime(string text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static bool TryReadMinutes(string text, out int minutes) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
        && minutes >= PomodoroTimer.MinMinutes && minutes <= PomodoroTimer.MaxMinutes;

    private static string Hm(DateTimeOffset value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Hm(TimeOnly value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static Brush Frozen(byte r, byte g, byte b, byte a)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>One valid form: the start, the end as typed, the lengths, the pomodoro count and when the last break ends.</summary>
    private readonly record struct FormValues(DateTimeOffset Start, DateTimeOffset End, int Focus, int Break, int Cycles,
        DateTimeOffset PlannedEnd);

    /// <summary>One row of the scheduled list, as bound by the item template.</summary>
    internal sealed record StartRow(Guid Id, string Day, string Time, string Mode, Brush ModeBrush, string Count);
}
