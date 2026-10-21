namespace Island.Core.Abstractions;

/// <summary>A Windows virtual desktop: its stable id, its name (never empty) and its position from the left, from 0.</summary>
public sealed record VirtualDesktopInfo(Guid Id, string Name, int Index);

/// <summary>
/// Windows virtual desktops. Implementations never throw to the caller: when the desktop API is missing or fails,
/// <see cref="IsAvailable"/> is false and the reads return null or an empty list, so the feature turns off instead of crashing.
/// There is no change notification: callers that need to know the current desktop read it again.
/// </summary>
public interface IVirtualDesktopService
{
    /// <summary>True when the desktop API works on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>All desktops in order. Empty when unavailable.</summary>
    IReadOnlyList<VirtualDesktopInfo> GetDesktops();

    /// <summary>Id of the desktop shown now, or null when it cannot be read.</summary>
    Guid? GetCurrentDesktopId();

    /// <summary>Shows the desktop with this id. False when it does not exist or the switch failed.</summary>
    bool SwitchTo(Guid desktopId);
}
