using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Island.App.Shell;
using Island.App.Widgets;
using Island.Core.Calendar;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using UserControl = System.Windows.Controls.UserControl;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Button = System.Windows.Controls.Button;

namespace Island.App.Views.Widgets;

/// <summary>Compact month calendar (280 x 152) backed by the shared local agenda.</summary>
public partial class CalendarWidget : UserControl
{
    private const int DayCount = 42;
    private const double TodayDisc = 16.0;

    private static readonly Brush Primary = Brushes.White;
    private static readonly Brush Secondary = Frozen(0xA1, 0xA1, 0xA6);
    private static readonly Brush Tertiary = Frozen(0x6E, 0x6E, 0x73);
    private static readonly Brush TodayFill = Frozen(0xFF, 0x45, 0x3A);
    private static readonly Brush SelectedFill = Frozen(0x0A, 0x84, 0xFF);
    private static readonly Brush MarkerFill = Frozen(0x30, 0xD1, 0x58);

    private readonly ShelfContext _context;
    private DateTime _today;
    private DateTime _shown;
    private DateOnly? _selectedDate;
    private bool _followToday = true;
    private bool _subscribed;
    private bool _isMonthTransition;

    public CalendarWidget(ShelfContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        InitializeComponent();
        _context = context;

        _today = DateTime.Today;
        _shown = FirstOfMonth(_today);

        PrevButton.Click += () => ChangeMonth(-1);
        NextButton.Click += () => ChangeMonth(1);
        Loaded += OnLoaded;
        Unloaded += (_, _) => Unsubscribe();

        Build();
    }

    private bool ReduceAnimations => _context.Settings().ReduceAnimations || !SystemParameters.ClientAreaAnimation;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _today = DateTime.Today;
        if (_followToday) _shown = FirstOfMonth(_today);
        Subscribe();
        Build();
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _context.Calendar.Changed += OnCalendarChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _context.Calendar.Changed -= OnCalendarChanged;
        _subscribed = false;
    }

    private void OnCalendarChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(Build));
            return;
        }
        Build();
    }

    private void ChangeMonth(int offset)
    {
        if (_isMonthTransition) return;
        if ((_shown.Year == 1 && offset < 0) || (_shown.Year == 9999 && offset > 0)) return;
        _shown = _shown.AddMonths(offset);
        _followToday = false;

        if (ReduceAnimations)
        {
            Build();
            return;
        }

        _isMonthTransition = true;
        var fadeOut = new DoubleAnimation(DayGrid.Opacity, 0.45, TimeSpan.FromMilliseconds(75))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        fadeOut.Completed += (_, _) =>
        {
            Build();
            var fadeIn = new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(135))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            fadeIn.Completed += (_, _) =>
            {
                DayGrid.BeginAnimation(UIElement.OpacityProperty, null);
                _isMonthTransition = false;
            };
            DayGrid.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };
        DayGrid.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void Build()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        DateTimeFormatInfo format = culture.DateTimeFormat;
        int firstDay = (int)format.FirstDayOfWeek;

        string month = culture.TextInfo.ToTitleCase(format.GetMonthName(_shown.Month));
        MonthTitle.Text = $"{month} {_shown.Year.ToString(culture)}";

        WeekdayGrid.Children.Clear();
        for (int i = 0; i < 7; i++)
        {
            string initial = format.ShortestDayNames[(firstDay + i) % 7];
            WeekdayGrid.Children.Add(MakeText(initial, Secondary, 11, FontWeights.Medium));
        }

        IReadOnlySet<DateOnly> markedDates = _context.Calendar.GetMarkedDates(_shown.Year, _shown.Month);
        DayGrid.Children.Clear();
        int lead = ((int)_shown.DayOfWeek - firstDay + 7) % 7;
        DateTime start = _shown.AddDays(-lead);
        for (int i = 0; i < DayCount; i++)
        {
            DateTime day = start.AddDays(i);
            DateOnly date = DateOnly.FromDateTime(day);
            bool inMonth = day.Month == _shown.Month && day.Year == _shown.Year;
            bool isToday = day.Date == _today.Date;
            bool isSelected = _selectedDate == date;
            CalendarDayItems dayItems = _context.Calendar.GetDay(date);
            bool hasItems = dayItems.Tasks.Count + dayItems.Birthdays.Count + dayItems.Events.Count > 0;
            bool hasMarker = markedDates.Contains(date) || hasItems;

            Brush foreground = isToday || isSelected ? Primary : inMonth ? Primary : Tertiary;
            FontWeight weight = isToday || isSelected ? FontWeights.SemiBold : FontWeights.Normal;
            var text = MakeText(day.Day.ToString(CultureInfo.InvariantCulture), foreground, 10.5, weight);
            var disc = new Border
            {
                Width = TodayDisc,
                Height = TodayDisc,
                CornerRadius = new CornerRadius(TodayDisc / 2.0),
                Background = isToday ? TodayFill : isSelected ? SelectedFill : Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = text,
            };
            var cell = new Grid { Width = 22, Height = 16 };
            cell.Children.Add(disc);
            if (hasMarker)
            {
                cell.Children.Add(new Ellipse
                {
                    Width = 3.5,
                    Height = 3.5,
                    Fill = MarkerFill,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 1, 0),
                });
            }

            var button = new Button
            {
                Style = (Style)FindResource("CalendarDayButtonStyle"),
                Content = cell,
                ToolTip = hasItems ? $"{date.ToDateTime(TimeOnly.MinValue).ToString("D", culture)} · {ItemSummary(dayItems)}" :
                    date.ToDateTime(TimeOnly.MinValue).ToString("D", culture),
            };
            string accessibleName = date.ToDateTime(TimeOnly.MinValue).ToString("D", culture)
                + (hasItems ? $". {ItemSummary(dayItems)}" : ". Sem itens cadastrados.");
            AutomationProperties.SetName(button, accessibleName);
            AutomationProperties.SetHelpText(button, hasItems ? "Abre a agenda deste dia." : "Abre este dia para criar itens.");
            button.Click += (_, _) => OpenDay(date);
            DayGrid.Children.Add(button);
        }
    }

    private static string ItemSummary(CalendarDayItems items)
    {
        int count = items.Tasks.Count + items.Birthdays.Count + items.Events.Count;
        return count == 1 ? "1 item no calendário." : $"{count} itens no calendário.";
    }

    private void OpenDay(DateOnly date)
    {
        _selectedDate = date;
        Build();
        CalendarDayWindow.ShowFor(_context.Calendar, date, ReduceAnimations, Window.GetWindow(this));
    }

    private static TextBlock MakeText(string text, Brush foreground, double size, FontWeight weight)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = size,
            FontWeight = weight,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "IslandFontFamily");
        return block;
    }

    private static DateTime FirstOfMonth(DateTime date) => new(date.Year, date.Month, 1);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
