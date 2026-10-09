using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Island.Core.Calendar;

namespace Island.App.Shell;

/// <summary>Creates one calendar item for the selected date.</summary>
public partial class CalendarEntryWindow : Window
{
    private static readonly string[] TimeFormats = ["HH:mm"];

    private readonly CalendarAgenda _agenda;
    private readonly DateOnly _date;
    private readonly CalendarEntryKind _kind;
    private readonly bool _reduceAnimations;

    private CalendarEntryWindow(CalendarAgenda agenda, DateOnly date, CalendarEntryKind kind, bool reduceAnimations)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        InitializeComponent();
        _agenda = agenda;
        _date = date;
        _kind = kind;
        _reduceAnimations = reduceAnimations;
        Opacity = reduceAnimations ? 1 : 0;

        bool isEvent = kind == CalendarEntryKind.Event;
        bool isBirthday = kind == CalendarEntryKind.Birthday;
        KindTitle.Text = kind switch
        {
            CalendarEntryKind.Task => "Nova tarefa",
            CalendarEntryKind.Event => "Novo evento",
            CalendarEntryKind.Birthday => "Novo aniversário",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        NameLabel.Text = isBirthday ? "Nome" : "Título";
        DateText.Text = date.ToDateTime(TimeOnly.MinValue).ToString("dddd, d 'de' MMMM 'de' yyyy", CultureInfo.CurrentCulture);
        EventOptions.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
        BirthdayInfo.Visibility = isBirthday ? Visibility.Visible : Visibility.Collapsed;
        NameBox.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(NameBox.Text)) NameError.Visibility = Visibility.Collapsed;
            SaveError.Visibility = Visibility.Collapsed;
        };
        TimeBox.TextChanged += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(TimeBox.Text)
                || TimeOnly.TryParseExact(TimeBox.Text.Trim(), TimeFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _))
            {
                TimeError.Visibility = Visibility.Collapsed;
                SaveError.Visibility = Visibility.Collapsed;
            }
        };

        Loaded += (_, _) =>
        {
            PlayEntrance();
            NameBox.Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            e.Handled = true;
            DialogResult = false;
        };
    }

    public static void ShowFor(CalendarAgenda agenda, DateOnly date, CalendarEntryKind kind,
        bool reduceAnimations, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        var window = new CalendarEntryWindow(agenda, date, kind, reduceAnimations);
        if (owner is not null) window.Owner = owner;
        window.ShowDialog();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        int useDark = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref useDark, sizeof(int));
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            NameError.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }
        NameError.Visibility = Visibility.Collapsed;
        TimeError.Visibility = Visibility.Collapsed;
        SaveError.Visibility = Visibility.Collapsed;

        TimeOnly? time = null;
        if (_kind == CalendarEntryKind.Event && !string.IsNullOrWhiteSpace(TimeBox.Text))
        {
            if (!TimeOnly.TryParseExact(TimeBox.Text.Trim(), TimeFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out TimeOnly parsedTime))
            {
                TimeError.Visibility = Visibility.Visible;
                TimeBox.Focus();
                return;
            }
            time = parsedTime;
        }

        try
        {
            switch (_kind)
            {
                case CalendarEntryKind.Task:
                    _agenda.AddTask(name, _date);
                    break;
                case CalendarEntryKind.Event:
                    _agenda.AddEvent(name, _date, time);
                    break;
                case CalendarEntryKind.Birthday:
                    _agenda.AddBirthday(name, _date.Month, _date.Day);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            SaveError.Text = "Não foi possível salvar este item. Verifique o acesso ao armazenamento e tente novamente.";
            SaveError.Visibility = Visibility.Visible;
        }
    }

    private void PlayEntrance()
    {
        if (_reduceAnimations)
        {
            Opacity = 1;
            return;
        }

        var offset = new TranslateTransform(0, 8);
        RenderTransform = offset;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
        offset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
