namespace Island.Core.GameNotes;

/// <summary>Turns a process name into the key that notes are stored under. Windows process names ignore case.</summary>
public static class GameKey
{
    public static string Normalize(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return string.Empty;

        string trimmed = processName.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4].TrimEnd();
        return trimmed.ToLowerInvariant();
    }
}
