using System.Text.RegularExpressions;

namespace SysGuard;

/// <summary>
/// Reconhecimento de conteúdo adulto pelo título da janela do navegador. É a última camada: quando o DNS
/// ou o hosts falham, a janela do navegador é minimizada assim que o título denuncia o site.
/// A lista é heurística de propósito — títulos neutros são cobertos pelo bloqueio de rede.
/// </summary>
internal static class AdultContent
{
    private const int MatchTimeoutMs = 100;
    private static readonly RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex[] Patterns =
    {
        // Palavras e termos que descrevem o conteúdo adulto.
        Pattern(@"\bporn\w*"),
        Pattern(@"\bxxx\w*"),
        Pattern(@"\bhentai\b"),
        Pattern(@"\bnsfw\b"),
        Pattern(@"\bnudes?\b"),
        Pattern(@"\bonlyfans\b"),
        Pattern(@"\bfansly\b"),
        Pattern(@"\bputaria\b"),
        Pattern(@"\bsafad[ao]s?\b"),
        Pattern(@"\bmasturba\w*"),
        Pattern(@"\bejacul\w*"),
        Pattern(@"\borgia\b"),
        Pattern(@"\bsuruba\b"),
        Pattern(@"\btransando\b"),
        Pattern(@"\bgozando\b"),
        Pattern(@"\bnovinhas\b"),
        Pattern(@"\bbrasileirinhas?\b"),
        Pattern(@"\bcaiu na net\b"),
        Pattern(@"\bsex ?cams?\b"),
        Pattern(@"\bcam ?girls?\b"),
        Pattern(@"\bsex ?chat\b"),
        Pattern(@"\bsex videos?\b"),
        Pattern(@"\bv[íi]deos? (?:de )?sexo\b"),
        Pattern(@"\bsexo (?:expl[íi]cito|amador|gr[áa]tis)\b"),
        Pattern(@"\badult ?friend ?finder\b"),
        // Nomes dos sites adultos mais conhecidos.
        Pattern(
            @"\b(?:xvideos|xnxx|xhamster|redtube|youporn|tube8|spankbang|eporner|chaturbate|stripchat|" +
            @"bongacams|camsoda|livejasmin|livejasmine|brazzers|bangbros|realitykings|naughtyamerica|" +
            @"motherless|tnaflix|thumbzilla|beeg|rule34|e-hentai|nhentai|hanime|fapello|erome|redgifs|" +
            @"tubegalore|sunporno|hqporner|manyvids|ashemaletube|youjizz|txxx|porntrex|thisvid|" +
            @"myfreecams|cam4|seancody|xtube|mofos|twistys|transangels|digitalplayground|pornhub)\b"),
    };

    /// <summary>Processos de navegador cujas janelas podem exibir os sites bloqueados.</summary>
    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "arc", "chromium",
        "iexplore", "zen", "librewolf", "waterfox", "thorium",
    };

    public static IReadOnlyList<Regex> AllPatterns => Patterns;

    /// <summary>True quando o título da janela indica conteúdo adulto.</summary>
    public static bool IsAdultTitle(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return false;

        foreach (var pattern in Patterns)
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

    /// <summary>True para o nome do processo de um navegador, com ou sem ".exe".</summary>
    public static bool IsBrowserProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return BrowserProcesses.Contains(name);
    }

    private static Regex Pattern(string pattern) =>
        new(pattern, Options, TimeSpan.FromMilliseconds(MatchTimeoutMs));
}
