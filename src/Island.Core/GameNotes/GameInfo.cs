namespace Island.Core.GameNotes;

/// <summary>
/// A game seen in the foreground. Notes are filed under <see cref="Key"/> (the normalized process name); the
/// display name and executable path are metadata that can change between runs without moving the notes.
/// </summary>
public sealed record GameInfo(string ProcessName, string DisplayName, string? ExePath)
{
    public string Key => GameKey.Normalize(ProcessName);
}
