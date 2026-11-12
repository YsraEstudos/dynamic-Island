namespace SysGuard;

/// <summary>
/// Bloco temporário do hosts com os sites de distração bloqueados enquanto a sessão do Pomodoro Angry
/// do Dynamic Island estiver ativa. Fica dentro do bloco gerenciado do SysGuard, com marcadores
/// próprios: ao terminar a sessão, a próxima composição do hosts simplesmente não inclui estas linhas.
/// <para>
/// O bloqueio vale o tempo todo (foco e intervalo); a única exceção é a janela final de 60 s de um
/// intervalo rodando, em que Twitter/X e Instagram saem do bloco. YouTube, Reddit e o filtro adulto
/// (camadas separadas) continuam valendo nessa janela.
/// </para>
/// </summary>
internal static class AngryDistractionHosts
{
    public const string BeginMarker = "# >>> DynamicIsland Angry: temporary distraction block >>>";
    public const string EndMarker = "# <<< DynamicIsland Angry <<<";

    /// <summary>
    /// Domínios de distração da mesma lista do Dynamic Island, com as variantes www/m e o encurtador
    /// do YouTube. Cobrir as variantes evita que o navegador caia numa delas por redirecionamento.
    /// </summary>
    public static IReadOnlyList<string> Domains { get; } =
    [
        "youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be",
        "twitter.com", "www.twitter.com", "mobile.twitter.com", "x.com", "www.x.com",
        "instagram.com", "www.instagram.com", "m.instagram.com",
        "reddit.com", "www.reddit.com", "m.reddit.com",
    ];

    /// <summary>
    /// Subconjunto liberado nos 60 s finais do intervalo: só Twitter/X e Instagram (com as variantes).
    /// É sempre um subconjunto de <see cref="Domains"/>.
    /// </summary>
    public static IReadOnlyList<string> ReleasedDomains { get; } =
    [
        "twitter.com", "www.twitter.com", "mobile.twitter.com", "x.com", "www.x.com",
        "instagram.com", "www.instagram.com", "m.instagram.com",
    ];

    private static readonly HashSet<string> ReleasedDomainSet = new(ReleasedDomains, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True quando o estado publicado pela ilha pede a guarda agora (a sessão inteira, inclusive o
    /// intervalo): o bloco do hosts precisa existir também durante a janela de liberação para que
    /// YouTube/Reddit não vazem.
    /// </summary>
    public static bool IsRequested(AngryLiveState state, DateTimeOffset now) => state.IsGuarding(now);

    /// <summary>
    /// Linhas completas do bloco (marcadores + IPv4/IPv6 por domínio) ou vazio quando não há sessão.
    /// Na janela de liberação do intervalo, emite todos os domínios menos <see cref="ReleasedDomains"/>.
    /// </summary>
    public static IReadOnlyList<string> BuildHostsSection(AngryLiveState state, DateTimeOffset now)
    {
        if (!IsRequested(state, now)) return Array.Empty<string>();
        return state.IsBreakRelease(now)
            ? BuildSectionLines(Domains.Where(domain => !ReleasedDomainSet.Contains(domain)))
            : BuildSectionLines();
    }

    /// <summary>Linhas do bloco completo, sem checar o estado (testes e diagnóstico).</summary>
    public static IReadOnlyList<string> BuildSectionLines() => BuildSectionLines(Domains);

    private static IReadOnlyList<string> BuildSectionLines(IEnumerable<string> domains)
    {
        var lines = new List<string>(Domains.Count * 2 + 2) { BeginMarker };
        foreach (var domain in domains)
        {
            lines.Add("0.0.0.0 " + domain);
            lines.Add(":: " + domain);
        }

        lines.Add(EndMarker);
        return lines;
    }
}
