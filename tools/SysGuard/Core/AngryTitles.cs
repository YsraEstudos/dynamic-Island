using System.Text.RegularExpressions;

namespace SysGuard;

/// <summary>
/// Títulos de janela que denunciam os sites de distração que o Pomodoro Angry bloqueia (YouTube,
/// Twitter/X, Instagram e Reddit). É a mesma heurística do Dynamic Island (<c>BlockedSites</c>), mantida
/// conservadora: o bloqueio de rede/hosts é a camada principal, esta só fecha a janela que aparecer.
/// <para>
/// <see cref="IsReleasedTitle"/> isola só Twitter/X e Instagram: esses dois títulos deixam de ser
/// minimizados nos 60 s finais do intervalo (os demais continuam sendo).
/// </para>
/// </summary>
internal static class AngryTitles
{
    private const int MatchTimeoutMs = 100;
    private static readonly RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>Twitter/X e Instagram: os únicos títulos liberados na janela final do intervalo.</summary>
    private static readonly Regex[] ReleasedPatterns =
    [
        Pattern(@"\bTwitter\b"),
        // Toda página do X termina com "/ X"; tweets usam "Nome on X: ...". O navegador decora o título
        // com texto variável depois, então só a fronteira depois de "/ X" é checada.
        Pattern(@"(?:^|\s)/ X(?=\s|$)"),
        Pattern(@"\bon X:"),
        Pattern(@"\bInstagram\b"),
    ];

    private static readonly Regex[] Patterns =
    [
        Pattern(@"\bYouTube\b"),
        .. ReleasedPatterns,
        Pattern(@"\bReddit\b"),
        Pattern(@"(?:^|[\s(])r/[A-Za-z0-9_]+"),
    ];

    /// <summary>True quando o título do navegador cita um site de distração do Angry.</summary>
    public static bool IsDistractionTitle(string? windowTitle) => MatchesAny(Patterns, windowTitle);

    /// <summary>
    /// True quando o título é de Twitter/X ou Instagram — as únicas distrações liberadas nos 60 s
    /// finais de um intervalo rodando. YouTube e Reddit continuam bloqueados nessa janela.
    /// </summary>
    public static bool IsReleasedTitle(string? windowTitle) => MatchesAny(ReleasedPatterns, windowTitle);

    private static bool MatchesAny(Regex[] patterns, string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return false;

        foreach (var pattern in patterns)
        {
            try
            {
                if (pattern.IsMatch(windowTitle)) return true;
            }
            catch (RegexMatchTimeoutException)
            {
                // Título patológico: trata como "sem correspondência".
            }
        }

        return false;
    }

    private static Regex Pattern(string pattern) =>
        new(pattern, Options, TimeSpan.FromMilliseconds(MatchTimeoutMs));
}
