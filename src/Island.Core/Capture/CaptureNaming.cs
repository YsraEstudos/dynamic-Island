using System.Globalization;

namespace Island.Core.Capture;

/// <summary>
/// File names for captures saved in the Capturas folder. The time stamp is ISO-like, so names sort by time.
/// </summary>
public static class CaptureNaming
{
    public const string ScreenshotPrefix = "Captura-";
    public const string ScreenshotExtension = ".png";
    public const string RecordingPrefix = "Gravacao-";
    public const string RecordingExtension = ".mp4";

    private const string TimeFormat = "yyyy-MM-dd_HH-mm-ss";

    public static string ScreenshotFileName(DateTime localTime) =>
        ScreenshotPrefix + localTime.ToString(TimeFormat, CultureInfo.InvariantCulture) + ScreenshotExtension;

    public static string RecordingFileName(DateTime localTime) =>
        RecordingPrefix + localTime.ToString(TimeFormat, CultureInfo.InvariantCulture) + RecordingExtension;

    /// <summary>
    /// Returns <paramref name="fileName"/> when it is free, otherwise appends " (2)", " (3)"... before the extension.
    /// </summary>
    public static string MakeUnique(string fileName, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(exists);
        if (!exists(fileName)) return fileName;

        string extension = Path.GetExtension(fileName);
        string stem = fileName[..^extension.Length];
        for (int n = 2; ; n++)
        {
            string candidate = $"{stem} ({n}){extension}";
            if (!exists(candidate)) return candidate;
        }
    }

    public static bool IsScreenshotName(string fileName) =>
        fileName.StartsWith(ScreenshotPrefix, StringComparison.OrdinalIgnoreCase)
        && fileName.EndsWith(ScreenshotExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The newest screenshot by write time, or null when there is none.</summary>
    public static string? LatestScreenshot(IEnumerable<(string FileName, DateTime WrittenUtc)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return files
            .Where(file => IsScreenshotName(file.FileName))
            .OrderByDescending(file => file.WrittenUtc)
            .Select(file => file.FileName)
            .FirstOrDefault();
    }
}
