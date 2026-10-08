namespace Island.Core.Models;

/// <summary>What the island shows right now. Immutable; produced by IslandStateReducer.</summary>
public sealed record IslandState(
    IslandMode Mode,
    MediaInfo? Media,
    VolumeInfo? Volume,
    bool Suspended,
    Notice? Notice = null)
{
    public static IslandState Initial { get; } = new(IslandMode.Compact, null, null, false, null);
}
