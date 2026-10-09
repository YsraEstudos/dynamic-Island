using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Island.Core.Notes;
using Brush = System.Windows.Media.Brush;

namespace Island.App.Notes;

/// <summary>How a note reads in lists and widget previews: title fallback, one-line preview, accent colour, age.</summary>
public static class QuickNoteDisplay
{
    private static readonly Brush DefaultAccent = Frozen(0x68, 0x70, 0x7E);
    private static readonly Brush BlueAccent = Frozen(0x65, 0xB6, 0xFF);
    private static readonly Brush GreenAccent = Frozen(0x69, 0xC9, 0x8E);
    private static readonly Brush YellowAccent = Frozen(0xF5, 0xC4, 0x51);
    private static readonly Brush PinkAccent = Frozen(0xF0, 0x8A, 0xB5);
    private static readonly Brush PurpleAccent = Frozen(0xBB, 0x9A, 0xF7);

    public static string Title(QuickNote note)
    {
        if (!string.IsNullOrWhiteSpace(note.Title)) return note.Title.Trim();
        string? firstLine = FirstLine(note.Content);
        if (firstLine is not null) return Trim(firstLine, 60);
        var firstItem = note.Checklist.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Text));
        return firstItem is not null ? Trim(firstItem.Text, 60) : "Sem título";
    }

    public static string Preview(QuickNote note)
    {
        // When the title was borrowed from the first content line, the preview continues after it.
        string content = note.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(note.Title) && FirstLine(content) is { } first)
        {
            int index = content.IndexOf(first, StringComparison.Ordinal);
            content = index >= 0 ? content[(index + first.Length)..] : string.Empty;
        }

        string flat = Flatten(content);
        if (flat.Length > 0) return flat;

        string checklist = ChecklistSummary(note);
        if (checklist.Length > 0) return checklist;

        return note.Tags.Count > 0 ? string.Join(" · ", note.Tags.Select(tag => "#" + tag)) : string.Empty;
    }

    public static string ChecklistSummary(QuickNote note)
    {
        if (note.Checklist.Count == 0) return string.Empty;
        int done = note.Checklist.Count(item => item.IsCompleted);
        return $"☑ {done}/{note.Checklist.Count} itens";
    }

    public static string TagsText(QuickNote note) =>
        string.Join("  ", note.Tags.Select(tag => "#" + tag));

    public static string Relative(DateTimeOffset moment, DateTimeOffset? now = null)
    {
        DateTimeOffset current = now ?? DateTimeOffset.Now;
        TimeSpan age = current - moment;
        if (age < TimeSpan.FromMinutes(1)) return "agora";
        if (age < TimeSpan.FromHours(1)) return $"há {(int)age.TotalMinutes} min";
        if (age < TimeSpan.FromHours(24)) return $"há {(int)age.TotalHours} h";
        if (age < TimeSpan.FromHours(48)) return "ontem";
        return moment.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture);
    }

    public static Brush Accent(QuickNoteColor color) => color switch
    {
        QuickNoteColor.Blue => BlueAccent,
        QuickNoteColor.Green => GreenAccent,
        QuickNoteColor.Yellow => YellowAccent,
        QuickNoteColor.Pink => PinkAccent,
        QuickNoteColor.Purple => PurpleAccent,
        _ => DefaultAccent
    };

    public static string ColorName(QuickNoteColor color) => color switch
    {
        QuickNoteColor.Blue => "Azul",
        QuickNoteColor.Green => "Verde",
        QuickNoteColor.Yellow => "Amarelo",
        QuickNoteColor.Pink => "Rosa",
        QuickNoteColor.Purple => "Roxo",
        _ => "Padrão"
    };

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed;
        }

        return null;
    }

    private static string Flatten(string text) =>
        string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Binds a whole <see cref="QuickNote"/> and reads one display aspect (ConverterParameter).</summary>
public sealed class QuickNoteDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not QuickNote note) return string.Empty;

        return (parameter as string) switch
        {
            "Title" => QuickNoteDisplay.Title(note),
            "Preview" => QuickNoteDisplay.Preview(note),
            "Tags" => QuickNoteDisplay.TagsText(note),
            "Time" => QuickNoteDisplay.Relative(note.UpdatedAt),
            "Accent" => QuickNoteDisplay.Accent(note.Color),
            "HasPreview" => QuickNoteDisplay.Preview(note).Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed,
            "HasTags" => note.Tags.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed,
            "Checklist" => QuickNoteDisplay.ChecklistSummary(note),
            "HasChecklist" => note.Checklist.Count > 0 && note.Content.Length > 0
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed,
            _ => string.Empty
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a <see cref="QuickNoteColor"/> to its accent brush (used by the colour swatches).</summary>
public sealed class QuickNoteColorBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        QuickNoteDisplay.Accent(value is QuickNoteColor color ? color : QuickNoteColor.Default);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Colour display name, for tooltips and automation names.</summary>
public sealed class QuickNoteColorNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        QuickNoteDisplay.ColorName(value is QuickNoteColor color ? color : QuickNoteColor.Default);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
