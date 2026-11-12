using System.Text;

namespace SysGuard;

/// <summary>
/// Manutenção do arquivo hosts: um bloco delimitado por marcadores, sempre precedido do conteúdo
/// original guardado em backup. A restauração devolve o arquivo exatamente como era.
/// </summary>
internal static class HostsFile
{
    public const string BeginMarker = "# >>> SysGuard protection (managed automatically) >>>";
    public const string EndMarker = "# <<< SysGuard protection <<<";

    /// <summary>Junta o conteúdo original com o bloco de bloqueio (idempotente).</summary>
    public static string Compose(string original, IEnumerable<string> sectionLines)
    {
        var builder = new StringBuilder(original.Length + 256 * 1024);

        var stripped = StripSection(original);
        if (stripped.Length > 0)
        {
            builder.Append(stripped.TrimEnd('\r', '\n'));
            builder.Append('\n');
        }

        builder.Append('\n').Append(BeginMarker).Append('\n');
        foreach (var line in sectionLines)
        {
            builder.Append(line).Append('\n');
        }

        builder.Append(EndMarker).Append('\n');
        return builder.ToString();
    }

    public static bool HasSection(string content) =>
        content.Contains(BeginMarker, StringComparison.Ordinal);

    /// <summary>
    /// Remove o bloco gerenciado (e a linha em branco de separação), preservando o resto. Sempre
    /// normaliza as quebras de linha para "\n", o que torna <see cref="Compose"/> idempotente.
    /// </summary>
    public static string StripSection(string content)
    {
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        if (!HasSection(normalized)) return normalized;

        var lines = normalized.Split('\n');
        var kept = new List<string>(lines.Length);
        var inside = false;
        foreach (var line in lines)
        {
            if (!inside && line.Trim() == BeginMarker)
            {
                inside = true;
                continue;
            }

            if (inside)
            {
                if (line.Trim() == EndMarker) inside = false;
                continue;
            }

            kept.Add(line);
        }

        // Tira a linha em branco extra que Compose coloca antes do bloco.
        while (kept.Count > 0 && kept[^1].Trim().Length == 0) kept.RemoveAt(kept.Count - 1);
        return kept.Count == 0 ? string.Empty : string.Join('\n', kept) + "\n";
    }

    /// <summary>
    /// Garante que o conteúdo atual (antes de qualquer bloqueio) está guardado. Se já existe backup,
    /// não sobrescreve: o original de uma instalação anterior continua valendo.
    /// </summary>
    public static void EnsureOriginalSnapshot(string hostsPath, string backupPath)
    {
        if (File.Exists(backupPath)) return;

        var directory = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var bytes = File.Exists(hostsPath) ? File.ReadAllBytes(hostsPath) : Array.Empty<byte>();
        var text = Encoding.UTF8.GetString(bytes);
        if (HasSection(text)) bytes = Encoding.UTF8.GetBytes(StripSection(text));

        File.WriteAllBytes(backupPath, bytes);
    }

    /// <summary>Aplica o bloco de bloqueio. Devolve false (e não lança) em erro de I/O.</summary>
    public static bool TryApply(string hostsPath, string backupPath, IReadOnlyList<string> sectionLines, out string? error)
    {
        error = null;
        try
        {
            EnsureOriginalSnapshot(hostsPath, backupPath);
            var original = File.Exists(backupPath)
                ? File.ReadAllText(backupPath, Encoding.UTF8)
                : string.Empty;
            var content = Compose(original, sectionLines);
            WritePreservingAttributes(hostsPath, Encoding.UTF8.GetBytes(content));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Error("Falha ao aplicar o arquivo hosts.", ex);
            return false;
        }
    }

    /// <summary>Volta o hosts ao original guardado (ou só remove o bloco, se o backup sumiu).</summary>
    public static bool TryRestore(string hostsPath, string backupPath, out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(backupPath))
            {
                WritePreservingAttributes(hostsPath, File.ReadAllBytes(backupPath));
                return true;
            }

            if (!File.Exists(hostsPath)) return true;
            var content = File.ReadAllText(hostsPath, Encoding.UTF8);
            if (!HasSection(content)) return true;
            WritePreservingAttributes(hostsPath, Encoding.UTF8.GetBytes(StripSection(content)));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Error("Falha ao restaurar o arquivo hosts.", ex);
            return false;
        }
    }

    private static void WritePreservingAttributes(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        FileAttributes attributes = 0;
        var existed = File.Exists(path);
        if (existed) attributes = File.GetAttributes(path);

        if (existed && (attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);

        try
        {
            var temp = path + ".sysguard.tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (existed && File.Exists(path)) File.SetAttributes(path, attributes);
        }
    }
}
