namespace Island.Core.Audio;

/// <summary>
/// One application with an audio session on the default output device.
/// <see cref="Volume"/> is 0-100, <see cref="Peak"/> is the smoothed peak 0-1, and <see cref="Active"/> is false when the
/// session exists but is not playing (shown faded).
/// </summary>
public sealed record AppAudioSession(
    string Id,
    string Name,
    int ProcessId,
    string ProcessName,
    int Volume,
    bool Muted,
    double Peak,
    bool Active,
    string? ExecutablePath = null);
