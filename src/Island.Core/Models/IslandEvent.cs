using Island.Core.Models;

namespace Island.Core.Models;

/// <summary>Input to the reducer/coordinator. Adapters convert OS signals into these.</summary>
public abstract record IslandEvent
{
    /// <summary>Volume or mute changed.</summary>
    public sealed record VolumeChanged(VolumeInfo Volume) : IslandEvent;

    /// <summary>Media session changed (null = no session). TrackChanged is true only when TrackKey differs from the previous one.</summary>
    public sealed record MediaChanged(MediaInfo? Media, bool TrackChanged) : IslandEvent;

    /// <summary>User asked to open the full player (click on the island).</summary>
    public sealed record ExpandRequested : IslandEvent;

    /// <summary>User chose "Customize Shelf" (enter edit mode).</summary>
    public sealed record CustomizeRequested : IslandEvent;

    /// <summary>User chose "Open Clipboard" (or pressed the hotkey).</summary>
    public sealed record ClipboardRequested : IslandEvent;

    /// <summary>Show a temporary toast.</summary>
    public sealed record NoticeRaised(Notice Notice) : IslandEvent;

    /// <summary>User/timeout asked to return to compact.</summary>
    public sealed record CollapseRequested : IslandEvent;

    /// <summary>User flicked the island away: shrink to the Mini pill.</summary>
    public sealed record MinimizeRequested : IslandEvent;

    /// <summary>Pointer entered/left the island or the user is dragging a control. While true, temporary states are held open.</summary>
    public sealed record InteractionChanged(bool IsInteracting) : IslandEvent;

    /// <summary>A foreground fullscreen app appeared/disappeared.</summary>
    public sealed record FullscreenChanged(bool IsFullscreen) : IslandEvent;

    /// <summary>User paused the island from the tray.</summary>
    public sealed record PausedChanged(bool IsPaused) : IslandEvent;

    /// <summary>Fired by the coordinator's single timer when a temporary state expires.</summary>
    public sealed record TemporaryStateExpired : IslandEvent;
}
