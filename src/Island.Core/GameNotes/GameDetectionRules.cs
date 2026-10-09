namespace Island.Core.GameNotes;

/// <summary>What the Windows adapter reads about the window that just became foreground. No Win32 types here.</summary>
public sealed record ForegroundProcess(
    string ProcessName,
    string? ExePath,
    string? FileDescription,
    string? WindowTitle,
    bool IsFullscreen,
    bool IsShellWindow,
    bool IsOwnProcess);

/// <summary>
/// Decides whether a foreground window is a game. Priority: a process listed in the game settings is always a game;
/// then the ignore list; then fullscreen or borderless windows count as games. Windowed apps are never games.
/// </summary>
public static class GameDetectionRules
{
    private const int MaxDisplayNameLength = 80;

    // Normalized keys (GameKey.Normalize). Shell, system and everyday apps: fullscreen does not make them games
    // (a browser video or a presentation is not something to keep notes for).
    private static readonly HashSet<string> IgnoredKeys = new(StringComparer.Ordinal)
    {
        "explorer", "dwm", "searchhost", "searchapp", "startmenuexperiencehost", "shellexperiencehost",
        "applicationframehost", "systemsettings", "textinputhost", "lockapp", "taskmgr", "sihost",
        "dynamicisland",
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "iexplore",
        "code", "devenv", "rider64", "idea64", "pycharm64", "notepad", "notepad++", "wordpad",
        "windowsterminal", "openconsole", "cmd", "powershell", "pwsh", "conhost",
        "winword", "excel", "powerpnt", "outlook", "onenote", "acrobat", "acrord32",
        "teams", "ms-teams", "slack", "discord", "spotify", "onedrive", "obs64", "vlc", "mpc-hc64",
    };

    public static bool IsIgnored(string? processName) => IgnoredKeys.Contains(GameKey.Normalize(processName));

    /// <summary>Returns the game for this foreground window, or null when it is not one.</summary>
    public static GameInfo? Detect(ForegroundProcess process, IReadOnlyList<string> configuredGames)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(configuredGames);

        if (process.IsOwnProcess || process.IsShellWindow) return null;

        string key = GameKey.Normalize(process.ProcessName);
        if (key.Length == 0) return null;

        bool configured = IsConfigured(key, configuredGames);
        if (!configured && (IgnoredKeys.Contains(key) || !process.IsFullscreen)) return null;

        return new GameInfo(process.ProcessName.Trim(), DisplayNameFor(process), process.ExePath);
    }

    private static bool IsConfigured(string key, IReadOnlyList<string> configuredGames)
    {
        foreach (string game in configuredGames)
        {
            if (GameKey.Normalize(game) == key) return true;
        }
        return false;
    }

    /// <summary>The file description ("Hollow Knight") is the best name; the window title is the fallback.</summary>
    internal static string DisplayNameFor(ForegroundProcess process)
    {
        string? name = FirstNonBlank(process.FileDescription, process.WindowTitle, process.ProcessName) ?? process.ProcessName;
        string trimmed = name.Trim();
        return trimmed.Length <= MaxDisplayNameLength ? trimmed : trimmed[..MaxDisplayNameLength];
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }
}
