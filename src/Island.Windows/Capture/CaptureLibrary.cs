using Island.Core.Capture;

namespace Island.Windows.Capture;

/// <summary>Where captures are saved and how the widget finds the newest one after a restart.</summary>
public static class CaptureLibrary
{
    /// <summary>%UserProfile%\Pictures\DynamicIsland\Capturas.</summary>
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures", "DynamicIsland", "Capturas");

    /// <summary>Full path of the newest screenshot in <paramref name="folder"/>, or null when there is none or the folder cannot be read.</summary>
    public static string? LatestScreenshot(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return null;
            var files = new DirectoryInfo(folder).EnumerateFiles().Select(file => (file.Name, file.LastWriteTimeUtc));
            string? name = CaptureNaming.LatestScreenshot(files);
            return name is null ? null : Path.Combine(folder, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
