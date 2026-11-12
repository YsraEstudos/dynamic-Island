namespace SysGuard;

/// <summary>
/// Bloco do hosts com os nomes dos resolvedores DNS-over-HTTPS conhecidos. Enquanto a proteção está
/// ativa, esses nomes apontam para 0.0.0.0, o que impede o navegador de cair num DoH fora do filtro
/// filtro adulto só por nome.
/// <para>
/// LIMITE HONESTO: hosts bloqueia nomes, não endereços. DoH por IP fixo (por exemplo
/// <c>https://1.1.1.1/dns-query</c>), VPN, proxy ou navegador portátil com resolvedor próprio continuam
/// furando. Isto é mitigação barata contra o caso comum (o navegador volta a usar o DNS do sistema,
/// que é o filtrado), não uma defesa completa.
/// </para>
/// </summary>
internal static class DohResolvers
{
    public const string BeginMarker = "# >>> SysGuard anti-DoH resolvers (name block) >>>";
    public const string EndMarker = "# <<< SysGuard anti-DoH resolvers <<<";

    /// <summary>Nomes conhecidos de endpoints DoH de resolvedores públicos.</summary>
    public static IReadOnlyList<string> Domains { get; } =
    [
        "dns.google", "dns.google.com",
        "cloudflare-dns.com", "mozilla.cloudflare-dns.com", "one.one.one.one",
        "1dot1dot1dot1.cloudflare-dns.com",
        "dns.quad9.net", "dns9.quad9.net", "dns10.quad9.net", "dns11.quad9.net",
        "doh.opendns.com", "doh.familyshield.opendns.com",
        "dns.adguard.com", "dns.adguard-dns.com",
        "dns.nextdns.io",
        "doh.cleanbrowsing.org",
        "dns.mullvad.net", "get.dns.mullvad.net",
        "doh.libredns.gr",
        "doh.pub", "dns.alidns.com",
        "doh.dns.sb", "dns.sb",
        "doh.tiar.app", "dns.tiar.app",
    ];

    /// <summary>Linhas do bloco (marcadores + IPv4/IPv6 por nome).</summary>
    public static IReadOnlyList<string> BuildHostsSection()
    {
        var lines = new List<string>(Domains.Count * 2 + 2) { BeginMarker };
        foreach (var domain in Domains)
        {
            lines.Add("0.0.0.0 " + domain);
            lines.Add(":: " + domain);
        }

        lines.Add(EndMarker);
        return lines;
    }
}
