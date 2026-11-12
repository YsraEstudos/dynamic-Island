using Microsoft.Win32;

namespace SysGuard;

/// <summary>Política que uma versão antiga do SysGuard gravava no registro (para migração/limpeza).</summary>
internal sealed record LegacyPolicy(RegistryHive Hive, string KeyPath, string ValueName)
{
    /// <summary>Descrição legível (hive + caminho + valor), usada em logs e testes.</summary>
    public string Description =>
        (Hive == RegistryHive.CurrentUser ? "HKCU\\" : "HKLM\\") + KeyPath + "\\" + ValueName;
}

/// <summary>
/// Políticas que versões anteriores gravavam e que a 1.1 não usa mais. A atualização precisa APAGAR o
/// valor antigo — não basta parar de escrever: o Chrome/Edge leem a política do registro ao iniciar e
/// continuariam em modo restrito do YouTube até alguém limpar. A limpeza cobre HKLM, o HKCU do
/// processo e as hives de usuário carregadas (HKU), porque o serviço roda como SYSTEM.
/// </summary>
internal static class LegacyPolicies
{
    /// <summary>Valor que forçava o YouTube Restricted Mode (o que fez o usuário perder resultados).</summary>
    public const string YouTubeRestrictValue = "ForceYouTubeRestrict";

    public static IReadOnlyList<LegacyPolicy> Entries { get; } =
    [
        new(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", YouTubeRestrictValue),
        new(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", YouTubeRestrictValue),
        new(RegistryHive.CurrentUser, @"SOFTWARE\Policies\Google\Chrome", YouTubeRestrictValue),
        new(RegistryHive.CurrentUser, @"SOFTWARE\Policies\Microsoft\Edge", YouTubeRestrictValue),
    ];

    /// <summary>Remove todas as entradas e devolve as que existiam (e foram apagadas). Nunca lança.</summary>
    public static IReadOnlyList<LegacyPolicy> RemoveAll() => RemoveAll(RemoveFromRegistry);

    /// <summary>
    /// Variante injetável (teste): <paramref name="remove"/> devolve true quando o valor existia e foi
    /// apagado. Devolve as entradas removidas.
    /// </summary>
    internal static IReadOnlyList<LegacyPolicy> RemoveAll(Func<LegacyPolicy, bool> remove)
    {
        ArgumentNullException.ThrowIfNull(remove);

        var removed = new List<LegacyPolicy>();
        foreach (var entry in Entries)
        {
            try
            {
                if (remove(entry)) removed.Add(entry);
            }
            catch (Exception ex)
            {
                Log.Info("Política: falha ao limpar valor legado " + entry.Description + ": " + ex.Message);
            }
        }

        if (removed.Count > 0)
        {
            Log.Info("Políticas: removidos valores legados que forçavam modo restrito: "
                     + string.Join("; ", removed.Select(entry => entry.Description)));
        }

        return removed;
    }

    private static bool RemoveFromRegistry(LegacyPolicy entry)
    {
        if (entry.Hive == RegistryHive.LocalMachine)
            return TryDelete(Registry.LocalMachine, entry.KeyPath, entry.ValueName);

        // O "HKCU" do serviço SYSTEM não é o do usuário logado: limpa o perfil atual e cada hive de
        // usuário carregada em HKU (cobre todos os usuários com sessão aberta).
        bool removed = TryDelete(Registry.CurrentUser, entry.KeyPath, entry.ValueName);
        foreach (var sid in SafeLoadedUserKeyNames())
        {
            removed |= TryDelete(Registry.Users, sid + "\\" + entry.KeyPath, entry.ValueName);
        }

        return removed;
    }

    private static bool TryDelete(RegistryKey root, string keyPath, string valueName)
    {
        try
        {
            // Sem CreateSubKey: uma chave que nunca existiu não precisa ser criada só para apagar.
            using var key = root.OpenSubKey(keyPath, writable: true);
            if (key?.GetValue(valueName) is null) return false;
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Bandeja comum não é admin; o serviço (SYSTEM) repete a limpeza no próximo ciclo.
            return false;
        }
    }

    private static string[] SafeLoadedUserKeyNames()
    {
        try
        {
            using var users = Registry.Users;
            return users.GetSubKeyNames()
                .Where(name => name is not (".DEFAULT" or "S-1-5-18" or "S-1-5-19" or "S-1-5-20"))
                .ToArray();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return Array.Empty<string>();
        }
    }
}
