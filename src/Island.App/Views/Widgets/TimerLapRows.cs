using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Island.Core.Tools.Timers;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Island.App.Views.Widgets;

/// <summary>
/// Two visible lap rows under the stopwatch, such as "Volta 3    00:12,45    01:02,10". The fastest lap is green and
/// the slowest red. The list is most recent first; the widget chooses the first visible lap and scrolls it.
/// </summary>
public sealed class TimerLapRows : Grid
{
    private const int VisibleRows = 2;
    private const double RowHeight = 15;

    private static readonly Brush BestBrush = Frozen(0x30, 0xD1, 0x58);
    private static readonly Brush WorstBrush = Frozen(0xFF, 0x45, 0x3A);
    private static readonly Brush PrimaryBrush = Frozen(0xF2, 0xF2, 0xF7);
    private static readonly Brush SecondaryBrush = Frozen(0xA1, 0xA1, 0xA6);
    private static readonly Brush TertiaryBrush = Frozen(0x6E, 0x6E, 0x73);

    public TimerLapRows()
    {
        Height = RowHeight * VisibleRows;
        for (int i = 0; i < VisibleRows; i++)
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        }
    }

    /// <summary>The largest first index that still fills every visible row.</summary>
    public static int MaxOffset(int lapCount) => Math.Max(0, lapCount - VisibleRows);

    /// <summary>Shows the laps from <paramref name="offset"/> on, in the order given.</summary>
    public void Show(IReadOnlyList<LapRecord> laps, int offset, LapRecord? best, LapRecord? worst)
    {
        Children.Clear();
        if (laps.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "Toque em Volta para marcar",
                FontSize = 11,
                Foreground = TertiaryBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRowSpan(hint, VisibleRows);
            Children.Add(hint);
            return;
        }

        for (int row = 0; row < VisibleRows && offset + row < laps.Count; row++)
        {
            Grid lapRow = BuildRow(laps[offset + row], best, worst);
            Grid.SetRow(lapRow, row);
            Children.Add(lapRow);
        }
    }

    private static Grid BuildRow(LapRecord lap, LapRecord? best, LapRecord? worst)
    {
        Brush lapBrush = lap.Number == best?.Number ? BestBrush
            : lap.Number == worst?.Number ? WorstBrush
            : PrimaryBrush;

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddCell(row, $"Volta {lap.Number}", SecondaryBrush, 0, HorizontalAlignment.Left);
        AddCell(row, TimerFormat.Stopwatch(lap.LapTime), lapBrush, 1, HorizontalAlignment.Left);
        AddCell(row, TimerFormat.Stopwatch(lap.TotalTime), SecondaryBrush, 2, HorizontalAlignment.Right);
        return row;
    }

    private static void AddCell(Grid row, string text, Brush foreground, int column, HorizontalAlignment alignment)
    {
        var cell = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = foreground,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Tabular digits keep the times from shifting width while they change.
        Typography.SetNumeralAlignment(cell, FontNumeralAlignment.Tabular);
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
