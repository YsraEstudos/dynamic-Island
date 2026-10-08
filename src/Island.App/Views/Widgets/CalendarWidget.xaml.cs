using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.App.Widgets;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using UserControl = System.Windows.Controls.UserControl;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Island.App.Views.Widgets;

/// <summary>
/// Month calendar (280 x 152). Static: no timers. Today is read once when the widget loads and marked with a red
/// disc. The previous and next buttons page through months; they are the only interaction.
/// </summary>
public partial class CalendarWidget : UserControl
{
    private const int DayCount = 42;
    private const double TodayDisc = 17.0;

    private static readonly Brush Primary = Brushes.White;
    private static readonly Brush Secondary = Frozen(0xA1, 0xA1, 0xA6);
    private static readonly Brush Tertiary = Frozen(0x6E, 0x6E, 0x73);
    private static readonly Brush TodayFill = Frozen(0xFF, 0x45, 0x3A);

    private DateTime _today;
    private DateTime _shown;
    private bool _followToday = true;

    public CalendarWidget()
    {
        InitializeComponent();

        _today = DateTime.Today;
        _shown = FirstOfMonth(_today);

        PrevButton.Click += () =>
        {
            _shown = _shown.AddMonths(-1);
            _followToday = false;
            Build();
        };
        NextButton.Click += () =>
        {
            _shown = _shown.AddMonths(1);
            _followToday = false;
            Build();
        };

        Loaded += (_, _) =>
        {
            _today = DateTime.Today;
            if (_followToday) _shown = FirstOfMonth(_today);
            Build();
        };

        Build();
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

        DayGrid.Children.Clear();
        int lead = ((int)_shown.DayOfWeek - firstDay + 7) % 7;
        DateTime start = _shown.AddDays(-lead);
        for (int i = 0; i < DayCount; i++)
        {
            DateTime day = start.AddDays(i);
            bool inMonth = day.Month == _shown.Month && day.Year == _shown.Year;
            bool isToday = day.Date == _today.Date;

            Brush foreground = isToday ? Primary : inMonth ? Primary : Tertiary;
            FontWeight weight = isToday ? FontWeights.SemiBold : FontWeights.Normal;
            TextBlock text = MakeText(day.Day.ToString(CultureInfo.InvariantCulture), foreground, 11, weight);

            var disc = new Border
            {
                Width = TodayDisc,
                Height = TodayDisc,
                CornerRadius = new CornerRadius(TodayDisc / 2.0),
                Background = isToday ? TodayFill : Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = text,
            };
            DayGrid.Children.Add(disc);
        }
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
