using System.Globalization;

namespace Island.Core.Capture;

/// <summary>Formats the recording timer: "mm:ss", or "h:mm:ss" from one hour on. Negative spans show as zero.</summary>
public static class ElapsedTime
{
    public static string Format(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        long totalSeconds = (long)elapsed.TotalSeconds;
        long hours = totalSeconds / 3600;
        long minutes = totalSeconds % 3600 / 60;
        long seconds = totalSeconds % 60;

        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{seconds:00}");
    }
}
