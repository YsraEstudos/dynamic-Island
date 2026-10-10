using Island.Core.Models;

namespace Island.Core.Application;

/// <summary>
/// Priority rules for which mode may replace which. Priority (high to low):
/// Customize > Clipboard = Expanded (user-opened, protected) > Notice = Volume (latest wins) > MediaPreview > Compact.
/// A lower-priority request never displaces a shown mode; an equal-priority request replaces it (latest wins),
/// and a request for the mode already shown restarts its timer.
/// Exception: an urgent notice (<see cref="Notice.Urgent"/>) replaces every mode except Customize, Mini included; Rank is unchanged.
/// </summary>
public static class EventPriorityPolicy
{
    public static int Rank(IslandMode mode) => mode switch
    {
        IslandMode.Mini => -1,
        IslandMode.Compact => 0,
        IslandMode.MediaPreview => 1,
        IslandMode.Volume => 2,
        IslandMode.Notice => 2,
        IslandMode.Expanded => 3,
        IslandMode.Clipboard => 3,
        IslandMode.Customize => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>The mode actually shown when <paramref name="requested"/> is asked for while <paramref name="current"/> is shown.</summary>
    public static IslandMode Resolve(IslandMode current, IslandMode requested) =>
        Rank(requested) >= Rank(current) ? requested : current;

    /// <summary>
    /// Modes owned by the coordinator's single timer: the temporary states only. Expanded, Clipboard and Customize have no
    /// timer: they stay until the user closes them (Expanded and Clipboard by a click outside the island).
    /// </summary>
    public static bool HasTimer(IslandMode mode) => mode is IslandMode.Volume or IslandMode.MediaPreview or IslandMode.Notice;
}
