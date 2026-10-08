namespace Island.Core.Models;

/// <summary>Snapshot of the current media session. Thumbnail is encoded image bytes (PNG/JPEG) or null.</summary>
public sealed record MediaInfo(
    string Title,
    string Artist,
    byte[]? Thumbnail,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    string? SourceApp = null)
{
    /// <summary>Identity of the track, ignoring playback position/state. Used to detect "track changed".</summary>
    public string TrackKey => $"{SourceApp}|{Artist}|{Title}";
}
