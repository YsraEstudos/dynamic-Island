using System.Globalization;

namespace Island.Core.Tools.TextAnalyzer;

/// <summary>Turns analyzer numbers into the pt-BR text the widget shows.</summary>
public static class TextStatsFormat
{
    private const int ShortTokenLimit = 10_000;
    private const int PreviewLength = 48;

    // The preview only reads the start of a text, so a multi-megabyte paste costs nothing on the UI thread.
    private const int PreviewScanLength = 200;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Digits with a pt-BR thousands separator: 1234 gives "1.234".</summary>
    public static string Number(long value) => value.ToString("N0", PtBr);

    /// <summary>"≈ N" for the estimate; from 10.000 it shortens to thousands: "≈ 1,2 mil".</summary>
    public static string Tokens(int tokens)
    {
        if (tokens < ShortTokenLimit) return "≈ " + Number(tokens);

        double thousands = tokens / 1000.0;
        string digits = thousands < 100
            ? Math.Round(thousands, 1).ToString("0.#", PtBr)
            : Math.Round(thousands).ToString("N0", PtBr);
        return "≈ " + digits + " mil";
    }

    /// <summary>"45 s", "2 min", "2 min 10 s", "1 h", "1 h 05 min". Seconds are dropped from hours.</summary>
    public static string Duration(TimeSpan time)
    {
        long total = Math.Max(0, (long)Math.Round(time.TotalSeconds, MidpointRounding.AwayFromZero));
        if (total < 60) return $"{total} s";

        if (total < 3600)
        {
            long minutes = total / 60;
            long seconds = total % 60;
            return seconds == 0 ? $"{minutes} min" : $"{minutes} min {seconds} s";
        }

        long hours = total / 3600;
        long rest = (total % 3600) / 60;
        return rest == 0 ? $"{hours} h" : $"{hours} h {rest:00} min";
    }

    /// <summary>The start of the text on one line, with a trailing ellipsis when it is cut.</summary>
    public static string Preview(string? text, int maxLength = PreviewLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string head = text.Length > PreviewScanLength ? text[..PreviewScanLength] : text;
        if (head.Length == PreviewScanLength && char.IsHighSurrogate(head[^1])) head = head[..^1];

        string oneLine = string.Join(' ', head.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (oneLine.Length <= maxLength && head.Length == text.Length) return oneLine;

        int cut = Math.Min(maxLength, oneLine.Length);
        if (cut > 0 && char.IsHighSurrogate(oneLine[cut - 1])) cut--;
        return oneLine[..cut].TrimEnd() + "…";
    }
}
