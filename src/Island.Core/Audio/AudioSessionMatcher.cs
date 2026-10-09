namespace Island.Core.Audio;

/// <summary>Finds sessions by process name, ignoring case and a ".exe" suffix ("Discord", "discord.exe" and "DISCORD" match).</summary>
public static class AudioSessionMatcher
{
    public static IReadOnlyList<AppAudioSession> FindByProcessName(IEnumerable<AppAudioSession> sessions, string processName)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        string wanted = Normalize(processName);
        if (wanted.Length == 0) return Array.Empty<AppAudioSession>();

        return sessions.Where(s => Normalize(s.ProcessName) == wanted).ToList();
    }

    public static string Normalize(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;

        string name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.ToLowerInvariant();
    }
}
