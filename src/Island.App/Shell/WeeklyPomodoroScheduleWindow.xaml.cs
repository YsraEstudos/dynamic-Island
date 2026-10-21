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

/// <summary>Edits repeating weekly Focus and Angry time blocks.</summary>
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
    private int _cycles;
    private bool _updating;

    public WeeklyPomodoroScheduleWindow(PomodoroWeeklySchedule schedule, int defaultCycles)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        InitializeComponent();
        _schedule = schedule;
        _cycles = Math.Clamp(defaultCycles, PomodoroTimer.MinCycles, PomodoroTimer.MaxCycles);
        ApplyCycles();

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
    }

    public static void ShowFor(PomodoroWeeklySchedule schedule, int defaultCycles)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (_open is not null)
        {
            _open.Activate();
            return;
        }

        var window = new WeeklyPomodoroScheduleWindow(schedule, defaultCycles);
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
        CyclesText.Text = _cycles.ToString(CultureInfo.InvariantCulture);
        MinusButton.IsEnabled = _cycles > PomodoroTimer.MinCycles;
        PlusButton.IsEnabled = _cycles < PomodoroTimer.MaxCycles;
    }

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        DayOfWeek[] days = _dayChecks.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();
        if (!TryTime(StartBox.Text, out TimeOnly start) || !TryTime(EndBox.Text, out TimeOnly end))
        {
            StatusText.Text = "Use horários no formato HH:mm, por exemplo 09:00.";
            return;
        }
        if (ModeBox.SelectedItem is not ComboBoxItem { Tag: string modeName }
            || !Enum.TryParse(modeName, out ScheduledStartMode mode) || !Enum.IsDefined(mode))
        {
            StatusText.Text = "Escolha Focus ou Angry.";
            return;
        }

        if (!_schedule.TryAddDays(days, start, end, mode, _cycles, NameBox.Text, out string error))
        {
            StatusText.Text = error;
            return;
        }

        StatusText.Text = days.Length == 1 ? "Faixa semanal adicionada." : $"Faixa adicionada a {days.Length} dias.";
        NameBox.Clear();
        RefreshBlocks();
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is Guid id) _schedule.Remove(id);
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
            return new BlockRow(block.Id, day,
                $"{block.Start.ToString("HH:mm", CultureInfo.InvariantCulture)}–{block.End.ToString("HH:mm", CultureInfo.InvariantCulture)}",
                mode, block.Cycles == 1 ? "1×" : $"{block.Cycles}×", block.Name,
                block.Mode == ScheduledStartMode.Angry ? Red : Orange);
        }).ToArray();
        BlocksList.ItemsSource = rows;
        EmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        BlocksScroll.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool TryTime(string text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private sealed record BlockRow(Guid Id, string Day, string Range, string Mode, string Cycles, string Name, Brush ModeBrush);
}
