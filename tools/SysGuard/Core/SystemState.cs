using System.Text;
using Microsoft.Win32;

namespace SysGuard;

/// <summary>
/// Junta as três camadas de proteção do sistema: hosts, DNS do filtro adulto e políticas de navegador.
/// O setup usa <see cref="Snapshot"/> antes de qualquer alteração; o serviço usa
/// <see cref="ApplyProtection"/> no watchdog e <see cref="RemoveProtection"/> ao desbloquear/parar.
/// </summary>
internal sealed class SystemState
{
    private readonly DnsController _dns;
    private readonly BrowserPolicies _policies;
    private readonly string _hostsPath;
    private readonly string _hostsBackupPath;
    private readonly IReadOnlyList<string> _hostsSection;
    private readonly Func<DateTimeOffset, IReadOnlyList<string>>? _angryHostsSection;
    private bool _angryBlockApplied;

    public SystemState(
        ICommandRunner runner,
        string hostsPath,
        string hostsBackupPath,
        string dnsStatePath,
        string policiesStatePath,
        IReadOnlyList<string>? hostsSection = null,
        Func<DateTimeOffset, IReadOnlyList<string>>? angryHostsSection = null)
    {
        _hostsPath = hostsPath;
        _hostsBackupPath = hostsBackupPath;
        _dns = new DnsController(runner, dnsStatePath);
        _policies = new BrowserPolicies(policiesStatePath);
        _hostsSection = hostsSection ?? BlockList.BuildHostsSection();
        _angryHostsSection = angryHostsSection;
    }

    public static SystemState CreateDefault() => new(
        new ProcessCommandRunner(),
        AppInfo.HostsPath,
        AppInfo.HostsBackupPath,
        AppInfo.DnsStatePath,
        AppInfo.PoliciesStatePath,
        angryHostsSection: now => AngryDistractionHosts.BuildHostsSection(AngryLiveCache.AllUsers.Read(now), now));

    /// <summary>Guarda hosts, DNS e políticas originais. Idempotente; nunca sobrescreve um backup existente.</summary>
    public void Snapshot()
    {
        HostsFile.EnsureOriginalSnapshot(_hostsPath, _hostsBackupPath);
        _dns.CaptureOriginals();
        _policies.Snapshot();
    }

    /// <summary>Aplica as três camadas. Devolve true só quando tudo foi aplicado com sucesso.</summary>
    public bool ApplyProtection()
    {
        bool hosts = EnsureHosts();
        bool dns = _dns.EnsureProtected();
        bool policies = _policies.EnsureApplied();
        return hosts && dns && policies;
    }

    /// <summary>Variante barata para o watchdog: hosts e políticas a cada ciclo; DNS só quando solicitado.</summary>
    public bool ApplyHostsAndPolicies()
    {
        bool hosts = EnsureHosts();
        bool policies = _policies.EnsureApplied();
        return hosts && policies;
    }

    /// <summary>Remove o bloqueio e devolve o sistema ao estado original.</summary>
    public bool RemoveProtection()
    {
        bool hosts = HostsFile.TryRestore(_hostsPath, _hostsBackupPath, out var hostsError);
        if (!hosts) Log.Error("Proteção: hosts não restaurado. " + hostsError);

        bool dns = _dns.Restore();
        bool policies = _policies.Restore();
        return hosts && dns && policies;
    }

    /// <summary>
    /// Reversão de emergência quando os arquivos de estado sumiram (obrigatório só em reparo de instalação
    /// quebrada): remove o que der sem saber os valores originais.
    /// </summary>
    public void EmergencyReset()
    {
        try
        {
            if (File.Exists(_hostsPath))
            {
                var content = File.ReadAllText(_hostsPath, Encoding.UTF8);
                if (HostsFile.HasSection(content))
                    File.WriteAllText(_hostsPath, HostsFile.StripSection(content), new UTF8Encoding(false));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Emergência: falha ao limpar o hosts.", ex);
        }

        try
        {
            // Sem o DNS original, deixa o adaptador usar DHCP (caso mais comum). Reconhece também os
            // IPs do filtro Family que a 1.0 usava, para limpar uma instalação antiga na atualização.
            var runner = new ProcessCommandRunner();
            foreach (var adapter in NetworkAdapters.Enumerate().Where(a => a.IsUp && a.HasGateway))
            {
                if (adapter.DnsV4.Any(DnsController.IsOurResolver))
                {
                    runner.Run(AppInfo.NsExePath,
                        $"interface ipv4 set dnsservers name=\"{adapter.Name}\" source=dhcp");
                }

                if (adapter.DnsV6.Any(DnsController.IsOurResolver))
                {
                    runner.Run(AppInfo.NsExePath,
                        $"interface ipv6 set dnsservers name=\"{adapter.Name}\" source=dhcp");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Emergência: falha ao restaurar o DNS.", ex);
        }

        // Políticas: apaga só os valores que são exatamente os nossos (não mexe em valores alheios).
        foreach (var entry in BrowserPolicies.Desired)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(entry.KeyPath, writable: true);
                if (key?.GetValue(entry.ValueName) is not { } current) continue;
                bool ours = entry.Kind == PolicyValueKind.Dword
                    ? current is int number && number == entry.DwordValue
                    : current is string text && text == entry.StringValue;
                if (ours) key.DeleteValue(entry.ValueName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                Log.Error($"Emergência: falha ao limpar política {entry.KeyPath}\\{entry.ValueName}.", ex);
            }
        }

        // A 1.0 gravava ForceYouTubeRestrict; a reversão de emergência também precisa limpar.
        LegacyPolicies.RemoveAll();
    }

    /// <summary>Diagnóstico: o bloco de bloqueio está presente no hosts?</summary>
    public bool IsHostsProtected()
    {
        try
        {
            return File.Exists(_hostsPath) && HostsFile.HasSection(File.ReadAllText(_hostsPath));
        }
        catch (Exception ex)
        {
            Log.Error("Proteção: falha ao checar o hosts.", ex);
            return false;
        }
    }

    /// <summary>
    /// Garante o bloco gerenciado do hosts: lista embutida + resolvedores DoH (sempre protegido) + bloco
    /// temporário do Angry, quando a ilha pede. O bloco do Angry é recalculado a cada chamada; quando a
    /// sessão acaba ele simplesmente deixa de ser composto, e a próxima gravação o remove.
    /// </summary>
    internal bool EnsureHosts()
    {
        try
        {
            HostsFile.EnsureOriginalSnapshot(_hostsPath, _hostsBackupPath);
            var original = File.Exists(_hostsBackupPath) ? File.ReadAllText(_hostsBackupPath, Encoding.UTF8) : string.Empty;
            var section = BuildSectionLines();
            var expected = HostsFile.Compose(original, section);

            var current = File.Exists(_hostsPath) ? File.ReadAllText(_hostsPath, Encoding.UTF8) : string.Empty;
            if (string.Equals(current, expected, StringComparison.Ordinal)) return true;

            if (!HostsFile.TryApply(_hostsPath, _hostsBackupPath, section, out var error))
                return LogHostsFailure(error);

            DnsController.FlushDnsCache();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Proteção: falha ao garantir o hosts.", ex);
            return false;
        }
    }

    /// <summary>Lista embutida + anti-DoH + bloco temporário do Angry (se ativo e travado agora).</summary>
    private List<string> BuildSectionLines()
    {
        var lines = new List<string>(_hostsSection.Count + (DohResolvers.Domains.Count * 2) + 40);
        lines.AddRange(_hostsSection);
        lines.AddRange(DohResolvers.BuildHostsSection());

        IReadOnlyList<string> angry = _angryHostsSection?.Invoke(DateTimeOffset.UtcNow) ?? Array.Empty<string>();
        bool applied = angry.Count > 0;
        if (applied) lines.AddRange(angry);

        if (applied != _angryBlockApplied)
        {
            _angryBlockApplied = applied;
            Log.Info(applied
                ? "Angry (Dynamic Island): bloqueio extra de distrações aplicado no hosts."
                : "Angry (Dynamic Island): sessão terminou; bloqueio extra removido do hosts.");
        }

        return lines;
    }

    private static bool LogHostsFailure(string? error)
    {
        Log.Error("Proteção: hosts não aplicado. " + error);
        return false;
    }
}
