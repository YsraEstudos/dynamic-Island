using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Island.Core.Pomodoro;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;

namespace Island.App.Shell;

/// <summary>
/// Edits repeating weekly Focus and Angry time blocks. Each block has a start and an end, and the focus and break lengths;
/// the number of pomodoros follows from them. "Editar" loads a block into the form and "Salvar faixa" changes it.
/// </summary>
public partial class WeeklyPomodoroScheduleWindow : Window
{
    private static readonly (DayOfWeek Day, string Label)[] Weekdays =
    [
        (DayOfWeek.Monday, "Seg"), (DayOfWeek.Tuesday, "Ter"), (DayOfWeek.Wednesday, "Qua"),
        (DayOfWeek.Thursday, "Qui"), (DayOfWeek.Friday, "Sex"), (DayOfWeek.Saturday, "Sáb"),
        (DayOfWeek.Sunday, "Dom"),
    ];
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];
    private static readonly Brush Orange = Frozen(0xFF, 0x9F, 0x0A);
    private static readonly Brush Red = Frozen(0xFF, 0x45, 0x3A);
    private static WeeklyPomodoroScheduleWindow? _open;

    private readonly PomodoroWeeklySchedule _schedule;
    private readonly Dictionary<DayOfWeek, CheckBox> _dayChecks = [];
    private bool _updating;
    // The block being edited, or null when the form adds new blocks.
    private Guid? _editingId;

    public WeeklyPomodoroScheduleWindow(PomodoroWeeklySchedule schedule, int defaultFocusMinutes, int defaultBreakMinutes)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        InitializeComponent();
        _schedule = schedule;
        FocusBox.Text = Math.Clamp(defaultFocusMinutes, PomodoroTimer.MinMinutes, PomodoroTimer.MaxMinutes).ToString(CultureInfo.InvariantCulture);
        BreakBox.Text = Math.Clamp(defaultBreakMinutes, PomodoroTimer.MinMinutes, PomodoroTimer.MaxMinutes).ToString(CultureInfo.InvariantCulture);

        foreach ((DayOfWeek day, string label) in Weekdays)
        {
            var check = new CheckBox
            {
                Content = label,
                Tag = day,
                Margin = new Thickness(0, 0, 14, 0),
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                ToolTip = Pt.DateTimeFormat.GetDayName(day),
                IsChecked = day == DateTime.Now.DayOfWeek,
            };
            _dayChecks.Add(day, check);
            DaysPanel.Children.Add(check);
        }

        _updating = true;
        EnabledCheck.IsChecked = schedule.Enabled;
        _updating = false;
        _schedule.Changed += OnScheduleChanged;
        Refresh();
        ApplyEditing();
        UpdateCountText();
    }

    public static void ShowFor(PomodoroWeeklySchedule schedule, int defaultFocusMinutes, int defaultBreakMinutes)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (_open is not null)
        {
            _open.Activate();
            return;
        }

        var window = new WeeklyPomodoroScheduleWindow(schedule, defaultFocusMinutes, defaultBreakMinutes);
        _open = window;
        try { window.ShowDialog(); }
        finally { _open = null; }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
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
        NameBox.Focus();
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _schedule.SetEnabled(EnabledCheck.IsChecked == true);
        Refresh();
    }

    private void OnSkipTomorrowClicked(object sender, RoutedEventArgs e)
    {
        _schedule.SkipNextDay();
        Refresh();
    }

    private void OnFormChanged(object sender, TextChangedEventArgs e) => UpdateCountText();

    /// <summary>Shows how many pomodoros the typed times and lengths fit, or why they do not fit.</summary>
    private void UpdateCountText()
    {
        if (!TryTime(StartBox.Text, out TimeOnly start) || !TryTime(EndBox.Text, out TimeOnly end)
            || !TryMinutes(FocusBox.Text, out int focus) || !TryMinutes(BreakBox.Text, out int brk))
        {
            CountText.Text = string.Empty;
            return;
        }
        if (start >= end)
        {
            CountText.Text = "O término precisa ser depois do início";
            return;
        }

        int cycles = PomodoroTimer.CyclesFor(end - start, focus, brk);
        CountText.Text = cycles < 1 ? "Não cabe um pomodoro"
            : cycles == 1 ? "1 pomodoro por faixa"
            : $"{cycles} pomodoros por faixa";
    }

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        DayOfWeek[] days = _dayChecks.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();
        if (!TryTime(StartBox.Text, out TimeOnly start) || !TryTime(EndBox.Text, out TimeOnly end))
        {
            StatusText.Text = "Use horários no formato HH:mm, por exemplo 09:00.";
            return;
        }
        if (!TryMinutes(FocusBox.Text, out int focus) || !TryMinutes(BreakBox.Text, out int brk))
        {
            StatusText.Text = $"Foco e pausa precisam ter entre {PomodoroTimer.MinMinutes} e {PomodoroTimer.MaxMinutes} minutos.";
            return;
        }
        if (ModeBox.SelectedItem is not ComboBoxItem { Tag: string modeName }
            || !Enum.TryParse(modeName, out ScheduledStartMode mode) || !Enum.IsDefined(mode))
        {
            StatusText.Text = "Escolha Focus ou Angry.";
            return;
        }

        bool saved;
        string error;
        if (_editingId is { } id)
        {
            if (days.Length != 1)
            {
                StatusText.Text = "Ao editar uma faixa, escolha um só dia da semana.";
                return;
            }
            saved = _schedule.TryUpdate(id, days[0], start, end, mode, focus, brk, NameBox.Text, out error);
            if (saved) StatusText.Text = "Faixa atualizada.";
        }
        else
        {
            saved = _schedule.TryAddDays(days, start, end, mode, focus, brk, NameBox.Text, out error);
            if (saved) StatusText.Text = days.Length == 1 ? "Faixa semanal adicionada." : $"Faixa adicionada a {days.Length} dias.";
        }

        if (!saved)
        {
            StatusText.Text = error;
            return;
        }

        _editingId = null;
        ApplyEditing();
        NameBox.Clear();
        RefreshBlocks();
    }

    /// <summary>Loads a block into the form so that "Salvar faixa" changes it.</summary>
    private void OnEditClicked(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not Guid id) return;
        PomodoroWeeklyBlock? block = _schedule.Blocks.FirstOrDefault(candidate => candidate.Id == id);
        if (block is null) return;

        _editingId = id;
        foreach ((DayOfWeek day, CheckBox check) in _dayChecks)
            check.IsChecked = day == block.Day;
        StartBox.Text = Hm(block.Start);
        EndBox.Text = Hm(block.End);
        FocusBox.Text = block.FocusMinutes.ToString(CultureInfo.InvariantCulture);
        BreakBox.Text = block.BreakMinutes.ToString(CultureInfo.InvariantCulture);
        NameBox.Text = block.Name;
        ModeBox.SelectedIndex = block.Mode == ScheduledStartMode.Angry ? 1 : 0;
        ApplyEditing();
        StatusText.Text = "Editando a faixa. Ajuste e clique em Salvar faixa.";
        NameBox.Focus();
    }

    private void OnCancelEditClicked(object sender, RoutedEventArgs e)
    {
        _editingId = null;
        ApplyEditing();
        NameBox.Clear();
        StatusText.Text = string.Empty;
    }

    /// <summary>The add button adds blocks, or saves the one being edited; Cancel edit appears only then.</summary>
    private void ApplyEditing()
    {
        bool editing = _editingId is not null;
        AddButton.Content = editing ? "Salvar faixa" : "Adicionar faixa";
        CancelEditButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not Guid id) return;

        if (_editingId == id)
        {
            _editingId = null;
            ApplyEditing();
        }
        _schedule.Remove(id);
        RefreshBlocks();
    }

    private void OnScheduleChanged()
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke(new Action(Refresh));
    }

    private void Refresh()
    {
        _updating = true;
        EnabledCheck.IsChecked = _schedule.Enabled;
        DateOnly tomorrow = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        bool skippedTomorrow = _schedule.SkippedDate == tomorrow;
        SkipTomorrowButton.IsEnabled = _schedule.Enabled && !skippedTomorrow;
        StatusText.Text = _schedule.HasPendingResumePrompt
            ? "A rotina está pausada até você escolher se deseja retomá-la."
            : skippedTomorrow ? "A rotina será ignorada amanhã; depois perguntaremos se deseja retomá-la."
            : !_schedule.Enabled ? "Rotina desativada."
            : string.Empty;
        _updating = false;
        RefreshBlocks();
    }

    private void RefreshBlocks()
    {
        var rows = _schedule.Blocks.Select(block =>
        {
            string day = Weekdays.FirstOrDefault(entry => entry.Day == block.Day).Label ?? "—";
            string mode = block.Mode == ScheduledStartMode.Angry ? "Angry" : "Focus";
            string cycles = block.Cycles == 1 ? "1×" : $"{block.Cycles}×";
            return new BlockRow(block.Id, day, $"{Hm(block.Start)}–{Hm(block.End)}", mode,
                $"{cycles} · {block.FocusMinutes}/{block.BreakMinutes} min", block.Name,
                block.Mode == ScheduledStartMode.Angry ? Red : Orange);
        }).ToArray();
        BlocksList.ItemsSource = rows;
        EmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        BlocksScroll.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool TryTime(string text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static bool TryMinutes(string text, out int minutes) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
        && minutes >= PomodoroTimer.MinMinutes && minutes <= PomodoroTimer.MaxMinutes;

    private static string Hm(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private sealed record BlockRow(Guid Id, string Day, string Range, string Mode, string Cycles, string Name, Brush ModeBrush);
}
