using System.Text;
using System.Text.RegularExpressions;

namespace SysGuard;

/// <summary>
/// Lista de domínios adultos embutida no executável (recurso <c>SysGuard.Resources.blocklist.txt</c>,
/// um domínio por linha). É uma camada extra: a filtragem principal é feita pelo DNS do filtro adulto
/// configurado no sistema, então a lista local não precisa conter a internet adulta inteira.
/// </summary>
internal static class BlockList
{
    public const string ResourceName = "SysGuard.Resources.blocklist.txt";

    private static readonly Regex ValidDomain = new(
        @"^[a-z0-9]([a-z0-9-]{0,62})?(\.[a-z0-9]([a-z0-9-]{0,62})?)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Lazy<string[]> LazyDomains = new(LoadDomains);

    public static IReadOnlyList<string> Domains => LazyDomains.Value;

    /// <summary>Linhas do bloco de hosts (IPv4 e IPv6 apontando para endereço nulo).</summary>
    public static IReadOnlyList<string> BuildHostsSection()
    {
        var lines = new List<string>(Domains.Count * 2);
        foreach (var domain in Domains)
        {
            lines.Add("0.0.0.0 " + domain);
            lines.Add(":: " + domain);
        }

        return lines;
    }

    private static string[] LoadDomains()
    {
        try
        {
            using var stream = typeof(BlockList).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                Log.Error($"Recurso de lista não encontrado: {ResourceName}.");
                return Array.Empty<string>();
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var domains = new List<string>(4096);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var domain = Normalize(line);
                if (domain is null) continue;
                if (seen.Add(domain)) domains.Add(domain);
            }

            return domains.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao carregar a lista de domínios.", ex);
            return Array.Empty<string>();
        }
    }

    /// <summary>Aceita linhas de hosts (<c>0.0.0.0 site</c>) ou domínio puro; devolve o domínio normalizado ou null.</summary>
    internal static string? Normalize(string? line)
    {
        if (line is null) return null;

        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#')) return null;

        // Formatos de hosts: "0.0.0.0 dominio", "127.0.0.1 dominio", "::1 dominio".
        var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var candidate = parts.Length switch
        {
            1 => parts[0],
            2 when parts[0] is "0.0.0.0" or "127.0.0.1" or "::" or "::1" => parts[1],
            _ => null,
        };

        if (candidate is null || candidate.Length > 100) return null;

        var domain = candidate.TrimEnd('.').ToLowerInvariant();
        if (domain.Length == 0 || !domain.Contains('.')) return null;
        if (domain is "localhost" or "localhost.localdomain" or "broadplatforms.net") return null;
        if (!ValidDomain.IsMatch(domain)) return null;
        if (domain.Contains("..")) return null;

        return domain;
    }
}
