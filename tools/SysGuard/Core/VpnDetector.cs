using System.Net;

namespace SysGuard;

/// <summary>
/// Detecção barata de VPN/túnel ativo e de DNS apontando para um proxy local (caso do Cloudflare WARP,
/// que põe <c>127.0.2.2/3</c> no adaptador). O SysGuard não derruba VPN sozinho: isso é decisão do dono
/// da rede e pode quebrar o trabalho dele; o objetivo é ALERTAR que a camada de DNS pode estar sendo
/// furada (red-team PC-17) no log, no STATUS e no SelfTest.
/// </summary>
internal static class VpnDetector
{
    /// <summary>Nomes/descrições conhecidos de clientes VPN (comparação contém, sem diferenciar caixa).</summary>
    private static readonly string[] KnownVpnNames =
    [
        "warp", "tailscale", "wireguard", "openvpn", "nordvpn", "nordlynx", "protonvpn", "expressvpn",
        "surfshark", "mullvad", "zerotier", "forticlient", "globalprotect", "anyconnect", "openconnect",
        "vpn", "tun", "tap",
    ];

    /// <summary>Detecta no ambiente real desta máquina (inclui adaptadores de túnel).</summary>
    public static IReadOnlyList<string> Detect() => Detect(NetworkAdapters.EnumerateAll());

    /// <summary>Função pura: devolve descrições curtas dos problemas encontrados (vazio = nada detectado).</summary>
    internal static IReadOnlyList<string> Detect(IReadOnlyList<AdapterDnsInfo> adapters)
    {
        var issues = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string description)
        {
            if (seen.Add(description)) issues.Add(description);
        }

        foreach (var adapter in adapters)
        {
            if (!adapter.IsUp) continue;

            if (adapter.IsTunnel)
            {
                Add("adaptador de túnel ativo (" + adapter.Name + ")");
                continue;
            }

            if (HasLoopbackDns(adapter))
            {
                Add("DNS proxy local, provável VPN/WARP, em '" + adapter.Name + "'");
                continue;
            }

            if (LooksLikeVpn(adapter.Name)) Add("adaptador VPN ativo (" + adapter.Name + ")");
        }

        return issues;
    }

    /// <summary>True quando o adaptador aponta o DNS para loopback (proxy DNS local, ex.: WARP).</summary>
    internal static bool HasLoopbackDns(AdapterDnsInfo adapter) =>
        adapter.DnsV4.Concat(adapter.DnsV6).Any(IsLoopbackAddress);

    private static bool IsLoopbackAddress(string value) =>
        IPAddress.TryParse(value, out var address) && IPAddress.IsLoopback(address);

    private static bool LooksLikeVpn(string name) =>
        KnownVpnNames.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
