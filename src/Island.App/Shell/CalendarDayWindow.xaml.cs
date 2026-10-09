using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using Island.Core.Calendar;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Button = System.Windows.Controls.Button;

namespace Island.App.Shell;

/// <summary>Lists every task, birthday and event for one date. Only one day window is kept open.</summary>
public partial class CalendarDayWindow : Window
{
    private static CalendarDayWindow? _open;

    private readonly CalendarAgenda _agenda;
    private readonly bool _reduceAnimations;
    private DateOnly _date;
    private string? _saveError;

    private CalendarDayWindow(CalendarAgenda agenda, DateOnly date, bool reduceAnimations)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        InitializeComponent();
        _agenda = agenda;
        _date = date;
        _reduceAnimations = reduceAnimations;

        CloseButton.Click += (_, _) => Close();
        AddTaskButton.Click += (_, _) => OpenEntry(CalendarEntryKind.Task);
        AddEventButton.Click += (_, _) => OpenEntry(CalendarEntryKind.Event);
        AddBirthdayButton.Click += (_, _) => OpenEntry(CalendarEntryKind.Birthday);
        _agenda.Changed += OnAgendaChanged;
        Closed += (_, _) =>
        {
            _agenda.Changed -= OnAgendaChanged;
            if (ReferenceEquals(_open, this)) _open = null;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            e.Handled = true;
            Close();
        };
        Loaded += (_, _) => PlayEntrance();
        Refresh();
    }

    public static void ShowFor(CalendarAgenda agenda, DateOnly date, bool reduceAnimations, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        if (_open is { IsVisible: true })
        {
            _open.SetDate(date);
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            _open.Activate();
            return;
        }

        var window = new CalendarDayWindow(agenda, date, reduceAnimations);
        if (owner is not null) window.Owner = owner;
        _open = window;
        window.Show();
        window.Activate();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        int useDark = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref useDark, sizeof(int));
    }

    private void SetDate(DateOnly date)
    {
        if (_date == date) return;
        _date = date;
        _saveError = null;
        Refresh();
    }

    private void OnAgendaChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(OnAgendaChanged));
            return;
        }
        _saveError = null;
        Refresh();
    }

    private void Refresh()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        DateTitle.Text = _date.ToDateTime(TimeOnly.MinValue).ToString("dddd, d 'de' MMMM 'de' yyyy", culture);
        CalendarDayItems items = _agenda.GetDay(_date);
        DayContent.Children.Clear();
        if (_saveError is not null)
        {
            DayContent.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x69, 0x5E)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock
                {
                    Text = _saveError,
                    Foreground = (Brush)FindResource("MutedRedBrush"),
                    TextWrapping = TextWrapping.Wrap,
                },
            });
        }

        bool hasItems = items.Tasks.Count + items.Birthdays.Count + items.Events.Count > 0;
        if (!hasItems)
        {
            DayContent.Children.Add(new Border
            {
                Background = (Brush)FindResource("ShelfTileBrush"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "Dia livre", FontSize = 16, FontWeight = FontWeights.SemiBold,
                            Foreground = (Brush)FindResource("TextPrimaryBrush") },
                        new TextBlock { Text = "Adicione uma tarefa, evento ou aniversário para esta data.",
                            Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap,
                            Foreground = (Brush)FindResource("TextSecondaryBrush") },
                    },
                },
            });
            return;
        }

        if (items.Tasks.Count > 0) DayContent.Children.Add(BuildSection("Tarefas", items.Tasks.Select(BuildTaskRow)));
        if (items.Birthdays.Count > 0) DayContent.Children.Add(BuildSection("Aniversários", items.Birthdays.Select(BuildBirthdayRow)));
        if (items.Events.Count > 0) DayContent.Children.Add(BuildSection("Eventos", items.Events.Select(BuildEventRow)));
    }

    private Border BuildSection(string title, IEnumerable<UIElement> rows)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(3, 0, 0, 5),
        });
        foreach (UIElement row in rows) content.Children.Add(row);
        return new Border
        {
            Background = (Brush)FindResource("ShelfTileBrush"),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(11, 10, 11, 11),
            Margin = new Thickness(0, 0, 0, 10),
            Child = content,
        };
    }

    private UIElement BuildTaskRow(CalendarTask task)
    {
        var indicator = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1.5),
            BorderBrush = task.IsCompleted ? (Brush)FindResource("AccentGreenBrush") : (Brush)FindResource("TextTertiaryBrush"),
            Background = task.IsCompleted ? (Brush)FindResource("AccentGreenBrush") : Brushes.Transparent,
            Child = task.IsCompleted ? new TextBlock
            {
                Text = "✓",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            } : null,
        };
        var title = new TextBlock
        {
            Text = task.Title,
            Margin = new Thickness(9, 0, 0, 0),
            Foreground = task.IsCompleted ? (Brush)FindResource("TextSecondaryBrush") : (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            TextDecorations = task.IsCompleted ? TextDecorations.Strikethrough : null,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(indicator);
        Grid.SetColumn(title, 1);
        row.Children.Add(title);
        var button = new Button { Style = (Style)FindResource("CalendarRowButtonStyle"), Content = row };
        AutomationProperties.SetName(button, task.IsCompleted ? $"Reabrir tarefa: {task.Title}" : $"Concluir tarefa: {task.Title}");
        button.Click += (_, _) =>
        {
            try { _agenda.SetTaskCompleted(task.Id, !task.IsCompleted); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                _saveError = "Não foi possível salvar a conclusão. Tente novamente.";
                Refresh();
            }
        };
        return button;
    }

    private UIElement BuildBirthdayRow(CalendarBirthday birthday) => BuildInfoRow(
        birthday.Name, "Aniversário", "♥", (Brush)FindResource("MutedRedBrush"));

    private UIElement BuildEventRow(CalendarEvent calendarEvent)
    {
        string time = calendarEvent.Time is { } value
            ? value.ToString("HH:mm", CultureInfo.InvariantCulture)
            : "Dia inteiro";
        return BuildInfoRow(calendarEvent.Title, time, "•", (Brush)FindResource("WaveBrush"));
    }

    private UIElement BuildInfoRow(string title, string detail, string symbol, Brush accent)
    {
        var badge = new Border
        {
            Width = 32,
            Height = 32,
            Background = (Brush)FindResource("ShelfButtonBrush"),
            CornerRadius = new CornerRadius(10),
            Child = new TextBlock
            {
                Text = symbol,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
        var labels = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        labels.Children.Add(new TextBlock
        {
            Text = detail,
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(badge);
        Grid.SetColumn(labels, 2);
        row.Children.Add(labels);
        return new Border { Padding = new Thickness(6, 5, 6, 5), Child = row };
    }

    private void OpenEntry(CalendarEntryKind kind)
    {
        bool reduceMotion = _reduceAnimations || !SystemParameters.ClientAreaAnimation;
        CalendarEntryWindow.ShowFor(_agenda, _date, kind, reduceMotion, this);
    }

    private void PlayEntrance()
    {
        if (_reduceAnimations)
        {
            Surface.Opacity = 1;
            SurfaceTranslation.Y = 0;
            return;
        }

        Surface.Opacity = 0;
        SurfaceTranslation.Y = 8;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        Surface.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(190)) { EasingFunction = easing });
        SurfaceTranslation.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = easing });
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
