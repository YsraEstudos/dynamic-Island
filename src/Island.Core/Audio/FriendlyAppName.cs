using System.Text;

namespace Island.Core.Audio;

/// <summary>
/// Turns what Windows knows about a session into a name for people: the executable's file description first ("Discord",
/// "Spotify"), then the session's own display name, then the process name split into words ("EpicGamesLauncher" becomes
/// "Epic Games Launcher").
/// </summary>
public static class FriendlyAppName
{
    public const string Fallback = "Aplicativo";

    public static string Resolve(string? fileDescription, string? sessionDisplayName, string? processName)
    {
        if (IsUsable(fileDescription)) return fileDescription!.Trim();
        if (IsUsable(sessionDisplayName)) return sessionDisplayName!.Trim();

        string words = SplitProcessName(processName);
        return words.Length > 0 ? words : Fallback;
    }

    /// <summary>Display names starting with '@' are resource references (MUI strings), not text a person should read.</summary>
    private static bool IsUsable(string? text) =>
        !string.IsNullOrWhiteSpace(text) && !text.TrimStart().StartsWith('@');

    /// <summary>Drops a trailing ".exe", splits camel case and separators, and capitalises each word.</summary>
    public static string SplitProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;

        string name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        var builder = new StringBuilder(name.Length + 8);
        bool startWord = true;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c is '_' or '-' or '.' || char.IsWhiteSpace(c))
            {
                if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');
                startWord = true;
                continue;
            }

            // A capital after a lowercase letter or digit starts a new word: "EpicGames" -> "Epic Games".
            if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])) && builder.Length > 0)
            {
                builder.Append(' ');
                startWord = true;
            }

            builder.Append(startWord ? char.ToUpperInvariant(c) : c);
            startWord = false;
        }

        return builder.ToString().Trim();
    }
}
