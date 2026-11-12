using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SysGuard;

/// <summary>Adaptador de rede relevante: nome da conexão (usado pelo netsh), estado e DNS efetivo.</summary>
internal sealed record AdapterDnsInfo(
    string Id,
    string Name,
    bool IsUp,
    bool HasGateway,
    string[] DnsV4,
    string[] DnsV6,
    bool IsTunnel = false);

/// <summary>DNS original de um adaptador, guardado antes de sobrescrevermos.</summary>
internal sealed record OriginalDnsRecord(string AdapterId, string AdapterName, string Family, bool HadStatic, string[] Servers);

/// <summary>
/// Configura o DNS de todos os adaptadores com rota padrão para o resolvedor **CleanBrowsing Adult**,
/// que bloqueia conteúdo adulto no DNS sem forçar o modo restrito do YouTube (o Family forçava SafeSearch
/// de vídeo e escondia resultados). SafeSearch de Google/Bing continua sendo forçado no próprio DNS do
/// Adult. O DNS original de cada adaptador é guardado antes da primeira alteração e restaurado depois.
/// </summary>
internal sealed class DnsController
{
    // IPs oficiais do CleanBrowsing Adult (docs.cleanbrowsing.org). O Family usava 185.228.168.168/.169.168
    // e 2a0d:2a00:1::/2a0d:2a00:2::; esses valores antigos são substituídos na próxima aplicação.
    public const string AdultV4Primary = "185.228.168.10";
    public const string AdultV4Secondary = "185.228.169.11";
    public const string AdultV6Primary = "2a0d:2a00:1::1";
    public const string AdultV6Secondary = "2a0d:2a00:2::1";

    public static readonly string[] AdultV4 = { AdultV4Primary, AdultV4Secondary };
    public static readonly string[] AdultV6 = { AdultV6Primary, AdultV6Secondary };

    /// <summary>IPs do filtro Family que a 1.0 usava: a migração/reversão de emergência precisa reconhecê-los.</summary>
    public static readonly string[] LegacyFamilyV4 = { "185.228.168.168", "185.228.169.168" };
    public static readonly string[] LegacyFamilyV6 = { "2a0d:2a00:1::", "2a0d:2a00:2::" };

    /// <summary>True quando o endereço é um resolvedor que este app configurou (Adult atual ou Family antigo).</summary>
    public static bool IsOurResolver(string value)
    {
        var normalized = NormalizeIp(value);
        return AdultV4.Concat(AdultV6).Concat(LegacyFamilyV4).Concat(LegacyFamilyV6)
            .Any(ours => string.Equals(NormalizeIp(ours), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private readonly ICommandRunner _runner;
    private readonly string _statePath;
    private readonly Func<IReadOnlyList<AdapterDnsInfo>> _adapterProvider;
    private readonly object _gate = new();
    private readonly Dictionary<string, OriginalDnsRecord> _originals = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _warningsShown = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public DnsController(ICommandRunner runner, string statePath, Func<IReadOnlyList<AdapterDnsInfo>>? adapterProvider = null)
    {
        _runner = runner;
        _statePath = statePath;
        _adapterProvider = adapterProvider ?? NetworkAdapters.Enumerate;
    }

    public IReadOnlyCollection<OriginalDnsRecord> Originals
    {
        get { lock (_gate) { EnsureLoadedLocked(); return _originals.Values.ToArray(); } }
    }

    /// <summary>Guarda o DNS original dos adaptadores atuais que ainda não estão registrados.</summary>
    public void CaptureOriginals()
    {
        lock (_gate)
        {
            EnsureLoadedLocked();
            foreach (var adapter in _adapterProvider())
            {
                CaptureLocked(adapter, "ipv4", adapter.DnsV4);
                CaptureLocked(adapter, "ipv6", adapter.DnsV6);
            }

            // Grava mesmo sem registros: a existência do arquivo é parte do estado da instalação.
            SaveLocked(force: true);
        }
    }

    /// <summary>
    /// Garante o DNS do filtro Adult nos adaptadores com rota padrão. Barato quando já está tudo certo
    /// (só leitura de propriedades); chama netsh apenas nos adaptadores divergentes.
    /// </summary>
    public bool EnsureProtected()
    {
        var adapters = _adapterProvider().Where(a => a.IsUp && a.HasGateway).ToArray();
        if (adapters.Length == 0)
        {
            Log.Info("DNS: nenhum adaptador ativo com rota padrão; aguardando.");
            return true;
        }

        var changed = false;
        var allOk = true;
        lock (_gate)
        {
            EnsureLoadedLocked();
            foreach (var adapter in adapters)
            {
                bool v4 = EnsureResolverLocked(adapter, "ipv4", AdultV4, adapter.DnsV4);
                bool v6 = EnsureResolverLocked(adapter, "ipv6", AdultV6, adapter.DnsV6);
                changed |= v4 || v6;
                allOk &= v4 && v6;
            }

            SaveLocked();
        }

        if (changed) FlushDns();
        if (allOk) VerifyApplied(adapters);
        return allOk;
    }

    /// <summary>Devolve o DNS de todos os adaptadores registrados ao que era antes da instalação.</summary>
    public bool Restore()
    {
        OriginalDnsRecord[] records;
        lock (_gate)
        {
            EnsureLoadedLocked();
            records = _originals.Values.ToArray();
        }

        var ok = true;
        foreach (var record in records)
        {
            bool applied = record is { HadStatic: true, Servers.Length: > 0 }
                ? SetStatic(record.AdapterName, record.Family, record.Servers)
                : ResetToDhcp(record.AdapterName, record.Family);
            ok &= applied;
            if (!applied) Log.Error($"DNS: não foi possível restaurar {record.AdapterName}/{record.Family}.");
        }

        FlushDns();
        return ok;
    }

    /// <summary>Lê o DNS original direto do registro (estático) ou marca como DHCP.</summary>
    internal static OriginalDnsRecord ReadOriginal(string adapterId, string adapterName, string family, string[] effectiveDns)
    {
        var (hadStatic, servers) = ReadStaticDns(adapterId, family, effectiveDns);
        return new OriginalDnsRecord(adapterId, adapterName, family, hadStatic, servers);
    }

    /// <summary>
    /// Decide o DNS original a partir do valor NameServer do registro. Sem NameServer o adaptador usa
    /// DNS do DHCP e a restauração precisa voltar para source=dhcp — não para um estático com os
    /// servidores efetivos (que era o bug em notebooks que trocam de rede).
    /// Exposto para teste sem tocar no registro real.
    /// </summary>
    internal static (bool HadStatic, string[] Servers) DecideOriginal(string? nameServerValue, string family, string[] effectiveDns)
    {
        if (string.IsNullOrWhiteSpace(nameServerValue)) return (false, Array.Empty<string>());

        var parsed = nameServerValue
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(IsIpAddress)
            .ToArray();
        if (parsed.Length > 0) return (true, parsed);

        // Estático com formato inesperado no registro: usa o DNS efetivo da família como fallback.
        var filtered = family == "ipv4"
            ? effectiveDns.Where(a => IPAddress.TryParse(a, out var address)
                && (address.AddressFamily == AddressFamily.InterNetwork || address.IsIPv4MappedToIPv6)).ToArray()
            : effectiveDns.Where(a => IPAddress.TryParse(a, out var address)
                && address.AddressFamily == AddressFamily.InterNetworkV6
                && !address.IsIPv4MappedToIPv6).ToArray();
        return (true, filtered);
    }

    private static (bool HadStatic, string[] Servers) ReadStaticDns(string adapterId, string family, string[] effectiveDns)
    {
        try
        {
            var id = adapterId.Trim();
            if (!id.StartsWith('{')) id = "{" + id + "}";
            var service = family == "ipv4" ? "Tcpip" : "Tcpip6";
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{service}\Parameters\Interfaces\{id}");
            return DecideOriginal(key?.GetValue("NameServer") as string, family, effectiveDns);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao ler o DNS original do registro.", ex);
            return (false, Array.Empty<string>());
        }
    }

    private bool EnsureResolverLocked(AdapterDnsInfo adapter, string family, string[] expected, string[] current)
    {
        CaptureLocked(adapter, family, current);
        if (SameServers(current, expected)) return true;

        if (!SetStatic(adapter.Name, family, expected))
        {
            WarnOnce(adapter.Name + family, $"DNS: falha ao configurar {family} em '{adapter.Name}'.");
            return false;
        }

        return true;
    }

    private void CaptureLocked(AdapterDnsInfo adapter, string family, string[] effectiveDns)
    {
        var key = adapter.Id + "|" + family;
        if (_originals.ContainsKey(key)) return;

        _originals[key] = ReadOriginal(adapter.Id, adapter.Name, family, effectiveDns);
    }

    /// <summary>Compara DNS normalizando IPv4 mapeado em IPv6 e a ordem dos servidores.</summary>
    internal static bool SameServers(IReadOnlyList<string> current, IReadOnlyList<string> expected)
    {
        var left = current.Where(IsIpAddress).Select(NormalizeIp).ToArray();
        var right = expected.Select(NormalizeIp).ToArray();
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    internal static string NormalizeIp(string value)
    {
        if (IPAddress.TryParse(value.Trim(), out var address))
        {
            if (address.IsIPv4MappedToIPv6) return address.MapToIPv4().ToString();
            return address.ToString();
        }

        return value.Trim().ToLowerInvariant();
    }

    private bool SetStatic(string adapterName, string family, IReadOnlyList<string> servers)
    {
        if (servers.Count == 0) return ResetToDhcp(adapterName, family);

        var args = $"interface {family} set dnsservers name=\"{adapterName}\" source=static " +
                   $"address={servers[0]} register=primary validate=no";
        var first = _runner.Run(AppInfo.NsExePath, args);
        if (!first.Success)
        {
            Log.Error($"DNS: netsh falhou para '{adapterName}' ({family}): {first.Output}");
            return false;
        }

        for (int i = 1; i < servers.Count; i++)
        {
            var add = _runner.Run(
                AppInfo.NsExePath,
                $"interface {family} add dnsservers name=\"{adapterName}\" address={servers[i]} index={i + 1} validate=no");
            if (!add.Success)
            {
                Log.Error($"DNS: netsh add falhou para '{adapterName}' ({family}): {add.Output}");
                return false;
            }
        }

        return true;
    }

    private bool ResetToDhcp(string adapterName, string family)
    {
        var result = _runner.Run(
            AppInfo.NsExePath,
            $"interface {family} set dnsservers name=\"{adapterName}\" source=dhcp");
        if (!result.Success) Log.Error($"DNS: netsh dhcp falhou para '{adapterName}' ({family}): {result.Output}");
        return result.Success;
    }

    /// <summary>Releitura barata (sem subprocesso) para confirmar que o DNS efetivo é o do filtro Adult.</summary>
    private void VerifyApplied(AdapterDnsInfo[] adapters)
    {
        try
        {
            var fresh = _adapterProvider().Where(a => adapters.Any(x => x.Id == a.Id)).ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var adapter in adapters)
            {
                if (!fresh.TryGetValue(adapter.Id, out var now)) continue;
                if (!SameServers(now.DnsV4, AdultV4) && now.DnsV4.Length > 0)
                    WarnOnce(adapter.Id + "v4", $"DNS: {adapter.Name} ainda não usa {AdultV4Primary}.");
                if (!SameServers(now.DnsV6, AdultV6) && now.DnsV6.Length > 0)
                    WarnOnce(adapter.Id + "v6", $"DNS: {adapter.Name} ainda não usa {AdultV6Primary} (IPv6).");
            }
        }
        catch (Exception ex)
        {
            Log.Error("DNS: falha ao reler os adaptadores.", ex);
        }
    }

    private void WarnOnce(string key, string message)
    {
        if (_warningsShown.Add(key)) Log.Info(message);
    }

    private void EnsureLoadedLocked()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (!File.Exists(_statePath)) return;
            foreach (var line in File.ReadAllLines(_statePath))
            {
                var parts = line.Split('\t');
                if (parts.Length != 5) continue;
                if (!bool.TryParse(parts[3], out bool hadStatic)) continue;
                var servers = parts[4].Length == 0
                    ? Array.Empty<string>()
                    : parts[4].Split(',', StringSplitOptions.RemoveEmptyEntries);
                var record = new OriginalDnsRecord(parts[0], parts[1], parts[2], hadStatic, servers);
                _originals[record.AdapterId + "|" + record.Family] = record;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao ler o estado de DNS salvo.", ex);
        }
    }

    private void SaveLocked(bool force = false)
    {
        if (_originals.Count == 0 && !force) return;
        try
        {
            var directory = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var lines = _originals.Values.Select(r => string.Join(
                '\t',
                r.AdapterId,
                r.AdapterName,
                r.Family,
                r.HadStatic.ToString(CultureInfo.InvariantCulture),
                string.Join(',', r.Servers)));

            var temp = _statePath + ".tmp";
            var content = _originals.Count == 0 ? string.Empty : string.Join('\n', lines) + "\n";
            File.WriteAllText(temp, content);
            File.Move(temp, _statePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao gravar o estado de DNS.", ex);
        }
    }

    private static bool IsIpAddress(string value) => IPAddress.TryParse(value.Trim(), out _);

    private static bool IsIpv4(string value) =>
        IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork;

    private static bool IsIpv6(string value)
    {
        if (!IPAddress.TryParse(value, out var address)) return false;
        return address.AddressFamily == AddressFamily.InterNetworkV6 && !address.IsIPv4MappedToIPv6;
    }

    /// <summary>
    /// Limpa o cache do cliente DNS do Windows (equivalente ao <c>ipconfig /flushdns</c>), chamado ao
    /// aplicar/restaurar o DNS e após mudar o hosts. Usa a API nativa e, se ela negar, cai para o
    /// utilitário do Windows (o serviço e o instalador rodam elevados; a bandeja comum só loga).
    /// </summary>
    internal static void FlushDnsCache()
    {
        bool native = false;
        try
        {
            native = DnsFlushResolverCache();
            if (native) return;
            Log.Info("DNS: DnsFlushResolverCache retornou false; usando ipconfig /flushdns.");
        }
        catch (Exception ex)
        {
            Log.Error("DNS: falha na API de flush; usando ipconfig /flushdns.", ex);
        }

        if (!native && !IsElevated())
        {
            Log.Info("DNS: sem elevação, ipconfig /flushdns seria negado; cache será limpo pelo serviço.");
            return;
        }

        try
        {
            var result = new ProcessCommandRunner().Run(
                Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), "/flushdns", 20_000);
            if (result.Success) Log.Info("DNS: ipconfig /flushdns executado.");
            else Log.Error("DNS: ipconfig /flushdns falhou: " + result.Output);
        }
        catch (Exception ex)
        {
            Log.Error("DNS: falha ao executar ipconfig /flushdns.", ex);
        }
    }

    /// <summary>Elevação do processo atual (o flush via ipconfig exige administrador).</summary>
    private static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void FlushDns() => FlushDnsCache();

    [System.Runtime.InteropServices.DllImport("dnsapi.dll", ExactSpelling = true)]
    private static extern bool DnsFlushResolverCache();
}

/// <summary>Leitura dos adaptadores de rede reais (sem pseudo-interfaces de filtros de driver).</summary>
internal static class NetworkAdapters
{
    /// <summary>
    /// Adaptadores normais (túneis de VPN ficam de fora por padrão: a camada de DNS só mexe em quem tem
    /// rota padrão). <see cref="EnumerateAll"/> inclui os túneis, para a detecção de VPN do PC-17.
    /// </summary>
    public static IReadOnlyList<AdapterDnsInfo> Enumerate() => EnumerateCore(includeTunnels: false);

    /// <summary>Todos os adaptadores, inclusive túneis (VPN/WARP/Tailscale), para detecção/alerta.</summary>
    public static IReadOnlyList<AdapterDnsInfo> EnumerateAll() => EnumerateCore(includeTunnels: true);

    private static IReadOnlyList<AdapterDnsInfo> EnumerateCore(bool includeTunnels)
    {
        var result = new List<AdapterDnsInfo>(4);
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao enumerar adaptadores de rede.", ex);
            return result;
        }

        foreach (var nic in interfaces)
        {
            try
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                bool isTunnel = nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel;
                if (isTunnel && !includeTunnels) continue;

                var props = nic.GetIPProperties();
                var dns = props.DnsAddresses
                    .Where(a => a is not null)
                    .Select(a => a.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                bool hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address is not null
                    && !g.Address.Equals(IPAddress.Any)
                    && !g.Address.Equals(IPAddress.IPv6Any));

                result.Add(new AdapterDnsInfo(
                    nic.Id,
                    nic.Name,
                    nic.OperationalStatus == OperationalStatus.Up,
                    hasGateway,
                    dns.Where(a => IPAddress.TryParse(a, out var address)
                        && (address.AddressFamily == AddressFamily.InterNetwork
                            || address.IsIPv4MappedToIPv6)).ToArray(),
                    dns.Where(a => IPAddress.TryParse(a, out var address)
                        && address.AddressFamily == AddressFamily.InterNetworkV6
                        && !address.IsIPv4MappedToIPv6).ToArray(),
                    isTunnel));
            }
            catch (Exception ex)
            {
                // Adaptadores virtuais podem falhar ao ler propriedades; não são relevantes.
                Log.Info($"Adaptador ignorado ({nic.Name}): {ex.Message}");
            }
        }

        return result;
    }
}
