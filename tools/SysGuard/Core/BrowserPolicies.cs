using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace SysGuard;

internal enum PolicyValueKind
{
    String,
    Dword,
}

internal sealed record PolicyEntry(
    string KeyPath,
    string ValueName,
    PolicyValueKind Kind,
    string StringValue = "",
    int DwordValue = 0);

/// <summary>
/// Políticas de navegador (HKLM) que desligam o DNS-over-HTTPS dos navegadores e forçam SafeSearch de
/// busca (Google/Bing). Sem essas políticas, Chrome/Edge/Firefox poderiam ignorar o DNS de bloqueio
/// adulto e buscar os sites direto.
/// <para>
/// A versão 1.1 removeu de propósito o <c>ForceYouTubeRestrict</c> (modo restrito do YouTube): ele
/// escondia resultados de vídeo que o usuário queria. Valores antigos gravados pela 1.0 são apagados
/// na aplicação e na restauração por <see cref="LegacyPolicies"/>.
/// </para>
/// Os valores originais são guardados antes da primeira escrita (tipo e conteúdo, inclusive tipos
/// exóticos como REG_EXPAND_SZ) e restaurados ao desbloquear/desinstalar.
/// </summary>
internal sealed class BrowserPolicies
{
    public static readonly IReadOnlyList<PolicyEntry> Desired = new PolicyEntry[]
    {
        // Chrome: DoH desligado, SafeSearch de busca do Google (sem restrição de vídeo do YouTube).
        new(@"SOFTWARE\Policies\Google\Chrome", "DnsOverHttpsMode", PolicyValueKind.String, "off"),
        new(@"SOFTWARE\Policies\Google\Chrome", "ForceGoogleSafeSearch", PolicyValueKind.Dword, DwordValue: 1),

        // Edge: DoH desligado, SafeSearch de busca do Bing e do Google (sem restrição de vídeo).
        new(@"SOFTWARE\Policies\Microsoft\Edge", "DnsOverHttpsMode", PolicyValueKind.String, "off"),
        new(@"SOFTWARE\Policies\Microsoft\Edge", "ForceBingSafeSearch", PolicyValueKind.Dword, DwordValue: 2),
        new(@"SOFTWARE\Policies\Microsoft\Edge", "ForceGoogleSafeSearch", PolicyValueKind.Dword, DwordValue: 1),

        // Firefox: DoH desligado e travado (sem Locked, o usuário poderia religar nas opções).
        new(@"SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS", "Enabled", PolicyValueKind.Dword, DwordValue: 0),
        new(@"SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS", "Locked", PolicyValueKind.Dword, DwordValue: 1),

        // Variantes Chromium que também leem políticas em seus próprios ramos.
        new(@"SOFTWARE\Policies\BraveSoftware\Brave", "DnsOverHttpsMode", PolicyValueKind.String, "off"),
        new(@"SOFTWARE\Policies\Chromium", "DnsOverHttpsMode", PolicyValueKind.String, "off"),
        new(@"SOFTWARE\Policies\Vivaldi", "DnsOverHttpsMode", PolicyValueKind.String, "off"),
    };

    private sealed record PolicySnapshot(
        string KeyPath,
        string ValueName,
        bool Existed,
        RegistryValueKind Kind,
        string EncodedValue);

    private readonly string _statePath;
    private readonly Dictionary<string, PolicySnapshot> _originals = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _loaded;

    public BrowserPolicies(string statePath) => _statePath = statePath;

    public static string StateKey(string keyPath, string valueName) => keyPath + "|" + valueName;

    /// <summary>Guarda os valores originais (só na primeira vez) e aplica os valores desejados.</summary>
    public void Snapshot()
    {
        lock (_gate)
        {
            EnsureLoadedLocked();
            var added = false;
            foreach (var entry in Desired)
            {
                var key = StateKey(entry.KeyPath, entry.ValueName);
                if (_originals.ContainsKey(key)) continue;
                _originals[key] = ReadCurrent(entry);
                added = true;
            }

            if (added || !File.Exists(_statePath)) SaveLocked();
        }
    }

    /// <summary>Reaplica o que estiver divergente. Devolve false quando algum valor não pôde ser escrito.</summary>
    public bool EnsureApplied()
    {
        var ok = true;
        foreach (var entry in Desired)
        {
            try
            {
                if (Matches(entry)) continue;
                if (!Write(entry)) ok = false;
            }
            catch (Exception ex)
            {
                Log.Error($"Política: falha ao aplicar {entry.KeyPath}\\{entry.ValueName}.", ex);
                ok = false;
            }
        }

        // Migração da 1.0: apaga o modo restrito do YouTube gravado pela versão antiga (HKLM + HKCU)
        // e esquece os "originais" dele no estado salvo.
        LegacyPolicies.RemoveAll();
        DropLegacySnapshots();
        return ok;
    }

    /// <summary>Devolve todos os valores guardados ao estado original (e apaga os valores legados).</summary>
    public bool Restore()
    {
        // Estado sem os legados: o modo restrito do YouTube da 1.0 não volta, nem como "original".
        DropLegacySnapshots();

        PolicySnapshot[] snapshots;
        lock (_gate)
        {
            EnsureLoadedLocked();
            snapshots = _originals.Values.ToArray();
        }

        LegacyPolicies.RemoveAll();

        if (snapshots.Length == 0 && File.Exists(_statePath))
        {
            // Estado de versão anterior/ilegível: sem os originais, remove só os valores que são
            // exatamente os nossos (não mexe em políticas alheias).
            Log.Info("Política: estado antigo ilegível; removendo apenas os valores definidos por nós.");
            return RemoveOwnValues();
        }

        var ok = true;
        foreach (var snapshot in snapshots)
        {
            try
            {
                if (!snapshot.Existed)
                {
                    // Não cria a chave para "apagar" um valor que nunca existiu (deixaria chaves vazias).
                    using var existing = Registry.LocalMachine.OpenSubKey(snapshot.KeyPath, writable: true);
                    if (existing?.GetValue(snapshot.ValueName) is not null)
                        existing.DeleteValue(snapshot.ValueName, throwOnMissingValue: false);
                    continue;
                }

                using var key = Registry.LocalMachine.CreateSubKey(snapshot.KeyPath, writable: true);
                if (key is null)
                {
                    ok = false;
                    continue;
                }

                WriteValue(key, snapshot.ValueName, snapshot.Kind, snapshot.EncodedValue);
            }
            catch (Exception ex)
            {
                Log.Error($"Política: falha ao restaurar {snapshot.KeyPath}\\{snapshot.ValueName}.", ex);
                ok = false;
            }
        }

        return ok;
    }

    /// <summary>Fallback de restauração: apaga apenas valores idênticos aos que este app define.</summary>
    private static bool RemoveOwnValues()
    {
        var ok = true;
        foreach (var entry in Desired)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(entry.KeyPath, writable: true);
                if (key?.GetValue(entry.ValueName) is not { } current) continue;
                bool ours = entry.Kind == PolicyValueKind.Dword
                    ? current is int number && number == entry.DwordValue
                    : current is string text && string.Equals(text, entry.StringValue, StringComparison.Ordinal);
                if (ours) key.DeleteValue(entry.ValueName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                Log.Error($"Política: falha ao remover valor antigo {entry.KeyPath}\\{entry.ValueName}.", ex);
                ok = false;
            }
        }

        // Sem o estado salvo ainda podemos limpar o modo restrito do YouTube que a 1.0 gravou.
        LegacyPolicies.RemoveAll();
        return ok;
    }

    private static bool Matches(PolicyEntry entry)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(entry.KeyPath);
            var current = key?.GetValue(entry.ValueName);
            return entry.Kind switch
            {
                PolicyValueKind.String => current is string text && string.Equals(text, entry.StringValue, StringComparison.Ordinal),
                PolicyValueKind.Dword => current is int number && number == entry.DwordValue,
                _ => false,
            };
        }
        catch (Exception ex)
        {
            Log.Info($"Política: falha ao ler {entry.KeyPath}\\{entry.ValueName}: {ex.Message}");
            return false;
        }
    }

    private static bool Write(PolicyEntry entry)
    {
        using var key = Registry.LocalMachine.CreateSubKey(entry.KeyPath, writable: true);
        if (key is null) return false;

        if (entry.Kind == PolicyValueKind.Dword)
            key.SetValue(entry.ValueName, entry.DwordValue, RegistryValueKind.DWord);
        else
            key.SetValue(entry.ValueName, entry.StringValue, RegistryValueKind.String);
        return true;
    }

    private static PolicySnapshot ReadCurrent(PolicyEntry entry)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(entry.KeyPath);
            if (key is null) return new PolicySnapshot(entry.KeyPath, entry.ValueName, false, RegistryValueKind.None, string.Empty);

            var value = key.GetValue(entry.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null) return new PolicySnapshot(entry.KeyPath, entry.ValueName, false, RegistryValueKind.None, string.Empty);

            var kind = key.GetValueKind(entry.ValueName);
            return new PolicySnapshot(entry.KeyPath, entry.ValueName, true, kind, Encode(value, kind));
        }
        catch (Exception ex)
        {
            Log.Error($"Política: falha ao ler valor original {entry.KeyPath}\\{entry.ValueName}.", ex);
            return new PolicySnapshot(entry.KeyPath, entry.ValueName, false, RegistryValueKind.None, string.Empty);
        }
    }

    private static string Encode(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.String or RegistryValueKind.ExpandString => (string)value,
        RegistryValueKind.DWord => ((int)value).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => ((long)value).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
        RegistryValueKind.MultiString => Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\u001F', (string[])value))),
        _ => value.ToString() ?? string.Empty,
    };

    private static void WriteValue(RegistryKey key, string name, RegistryValueKind kind, string encoded)
    {
        switch (kind)
        {
            case RegistryValueKind.String:
                key.SetValue(name, encoded, RegistryValueKind.String);
                break;
            case RegistryValueKind.ExpandString:
                key.SetValue(name, encoded, RegistryValueKind.ExpandString);
                break;
            case RegistryValueKind.DWord:
                key.SetValue(name, int.Parse(encoded, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
                break;
            case RegistryValueKind.QWord:
                key.SetValue(name, long.Parse(encoded, CultureInfo.InvariantCulture), RegistryValueKind.QWord);
                break;
            case RegistryValueKind.Binary:
                key.SetValue(name, Convert.FromBase64String(encoded), RegistryValueKind.Binary);
                break;
            case RegistryValueKind.MultiString:
                var joined = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                key.SetValue(name, joined.Split('\u001F'), RegistryValueKind.MultiString);
                break;
            default:
                key.SetValue(name, encoded, RegistryValueKind.String);
                break;
        }
    }

    /// <summary>
    /// Esquece no estado salvo os "originais" das políticas legadas que a 1.0 gravava: assim a
    /// restauração/desinstalação não reintroduz o modo restrito do YouTube (nem como valor antigo).
    /// </summary>
    private void DropLegacySnapshots()
    {
        lock (_gate)
        {
            EnsureLoadedLocked();
            bool dropped = false;
            foreach (var entry in LegacyPolicies.Entries)
                dropped |= _originals.Remove(StateKey(entry.KeyPath, entry.ValueName));

            if (!dropped) return;
            SaveLocked();
            Log.Info("Políticas: estado salvo atualizado sem os valores legados do modo restrito do YouTube.");
        }
    }

    private void EnsureLoadedLocked()
    {
        if (_loaded) return;
        _loaded = true;
        BrowserPoliciesSerializer.Load(_statePath, _originals);
    }

    private void SaveLocked() => BrowserPoliciesSerializer.Save(_statePath, _originals.Values);

    private static class BrowserPoliciesSerializer
    {
        public static void Load(string path, Dictionary<string, PolicySnapshot> target)
        {
            try
            {
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var parts = line.Split('\t');
                    if (parts.Length != 5) continue;
                    if (!bool.TryParse(parts[2], out bool existed)) continue;
                    if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kindCode)) continue;
                    var encoded = Decode(parts[4]);
                    target[StateKey(parts[0], parts[1])] = new PolicySnapshot(
                        parts[0], parts[1], existed, (RegistryValueKind)kindCode, encoded);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao ler o estado de políticas salvo.", ex);
            }
        }

        public static void Save(string path, IEnumerable<PolicySnapshot> snapshots)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                var lines = new List<string>();
                foreach (var snapshot in snapshots)
                {
                    lines.Add(string.Join(
                        '\t',
                        snapshot.KeyPath,
                        snapshot.ValueName,
                        snapshot.Existed.ToString(CultureInfo.InvariantCulture),
                        ((int)snapshot.Kind).ToString(CultureInfo.InvariantCulture),
                        Encode(snapshot.EncodedValue)));
                }

                var temp = path + ".tmp";
                File.WriteAllText(temp, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao gravar o estado de políticas.", ex);
            }
        }

        private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        private static string Decode(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }
    }
}
