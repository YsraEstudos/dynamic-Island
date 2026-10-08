using System.Text.RegularExpressions;

namespace Island.Core.Pomodoro;

/// <summary>A site the angry focus guard blocks, recognised by the window titles a browser shows for it.</summary>
public sealed record BlockedSite(string Name, IReadOnlyList<Regex> TitlePatterns);

/// <summary>Sites blocked during angry focus, matched by window title, and the browser processes that can show them.</summary>
public static class BlockedSites
{
    // Window titles can be arbitrary text; a timeout turns a pathological title into "no match" instead of a stall.
    private const int MatchTimeoutMs = 100;
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly BlockedSite[] Sites =
    {
        new BlockedSite("YouTube", new[]
        {
            Pattern(@"\bYouTube\b"),
        }),
        new BlockedSite("Twitter/X", new[]
        {
            Pattern(@"\bTwitter\b"),
            // Every X page title ends with "/ X", and tweet titles use "Name on X: ...". The browser decoration
            // after "/ X" varies by browser and language (" - Google Chrome", " and 10 more pages - Edge", ...),
            // so only the token boundary after "/ X" is checked.
            Pattern(@"(?:^|\s)/ X(?=\s|$)"),
            Pattern(@"\bon X:"),
        }),
        new BlockedSite("Instagram", new[]
        {
            Pattern(@"\bInstagram\b"),
        }),
        new BlockedSite("Reddit", new[]
        {
            Pattern(@"\bReddit\b"),
            Pattern(@"(?:^|[\s(])r/[A-Za-z0-9_]+"),
        }),
    };

    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "arc", "chromium",
        "iexplore", "zen", "librewolf", "waterfox", "thorium",
    };

    public static IReadOnlyList<BlockedSite> Default { get; } = Sites;

    /// <summary>Name of the first site with a title pattern matching <paramref name="windowTitle"/>, or null.</summary>
    public static string? Match(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return null;

        foreach (var site in Sites)
        {
            foreach (var pattern in site.TitlePatterns)
            {
                if (IsMatch(pattern, windowTitle)) return site.Name;
            }
        }

        return null;
    }

    /// <summary>True for a browser's process name, with or without a ".exe" suffix, in any letter case.</summary>
    public static bool IsBrowserProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return BrowserProcesses.Contains(name);
    }

    private static Regex Pattern(string pattern) =>
        new(pattern, Options, TimeSpan.FromMilliseconds(MatchTimeoutMs));

    private static bool IsMatch(Regex pattern, string text)
    {
        try
        {
            return pattern.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
