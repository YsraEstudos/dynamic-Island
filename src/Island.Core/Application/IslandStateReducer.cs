using Island.Core.Configuration;
using Island.Core.Models;

namespace Island.Core.Application;

/// <summary>What the timer should do after a reduction. Keep leaves any pending timer untouched.</summary>
public enum TimerAction
{
    Keep,
    Arm,
    Cancel,
}

/// <summary>
/// Inputs that are not visible in <see cref="IslandState"/> but decide what it may become.
/// Fullscreen is kept raw (not pre-combined with settings) so a live settings change applies to the next event.
/// </summary>
public sealed record ReducerFlags(bool Fullscreen, bool Paused, bool Interacting)
{
    public static ReducerFlags None { get; } = new(false, false, false);
}

public sealed record ReductionResult(IslandState State, ReducerFlags Flags, TimerAction Timer, TimeSpan Delay);

/// <summary>Pure state machine. No I/O, no timers, no shared state.</summary>
public static class IslandStateReducer
{
    public static bool IsSuspended(ReducerFlags flags, IslandSettings s) =>
        flags.Paused || (flags.Fullscreen && s.HideInFullscreen);

    public static ReductionResult Reduce(IslandState state, ReducerFlags flags, IslandEvent e, IslandSettings s)
    {
        var mode = state.Mode;
        var media = state.Media;
        var volume = state.Volume;
        var notice = state.Notice;
        // True when this event (re)starts the timer belonging to the resulting mode.
        var restart = false;

        switch (e)
        {
            case IslandEvent.VolumeChanged v:
                volume = v.Volume;
                if (s.ShowVolume) Request(ref mode, ref restart, IslandMode.Volume);
                break;

            case IslandEvent.MediaChanged m:
                media = m.Media;
                if (media is null)
                {
                    if (mode is IslandMode.MediaPreview) mode = IslandMode.Compact;
                }
                // Playback ticks (TrackChanged == false) fall through here untouched: no mode change, no restart.
                else if (m.TrackChanged && media.IsPlaying && s.ShowMedia)
                {
                    Request(ref mode, ref restart, IslandMode.MediaPreview);
                }
                break;

            case IslandEvent.ExpandRequested:
                // The shelf holds other widgets too, so it opens without media. Also the way out of Customize and Clipboard.
                mode = IslandMode.Expanded;
                restart = true;
                break;

            case IslandEvent.CustomizeRequested:
                Request(ref mode, ref restart, IslandMode.Customize);
                break;

            case IslandEvent.ClipboardRequested:
                if (s.ClipboardEnabled) Request(ref mode, ref restart, IslandMode.Clipboard);
                break;

            case IslandEvent.NoticeRaised n:
                Request(ref mode, ref restart, IslandMode.Notice);
                // A notice that does not resolve to Notice (a protected mode is shown) is dropped.
                if (mode == IslandMode.Notice) notice = n.Notice;
                break;

            case IslandEvent.CollapseRequested:
                mode = IslandMode.Compact;
                break;

            case IslandEvent.MinimizeRequested:
                mode = IslandMode.Mini;
                break;

            case IslandEvent.InteractionChanged i:
                flags = flags with { Interacting = i.IsInteracting };
                // Ending an interaction restarts the mode's timer with its full duration.
                restart = !i.IsInteracting;
                break;

            case IslandEvent.FullscreenChanged f:
                flags = flags with { Fullscreen = f.IsFullscreen };
                break;

            case IslandEvent.PausedChanged p:
                flags = flags with { Paused = p.IsPaused };
                break;

            case IslandEvent.TemporaryStateExpired:
                // Timer-owned: temporary states and the idle collapse of Expanded/Clipboard all return to Compact.
                if (!flags.Interacting && mode is not (IslandMode.Customize or IslandMode.Mini)) mode = IslandMode.Compact;
                break;
        }

        var suspended = IsSuspended(flags, s);
        // A suspended island is hidden; it comes back as it was if it was a Mini pill, otherwise as Compact.
        if (suspended && mode != IslandMode.Mini) mode = IslandMode.Compact;
        // The notice is only meaningful while it is the shown mode.
        if (mode != IslandMode.Notice) notice = null;

        var next = state with { Mode = mode, Media = media, Volume = volume, Notice = notice, Suspended = suspended };

        // Invariant: a timer runs only for a timer-owned mode that is not being interacted with.
        if (!EventPriorityPolicy.HasTimer(mode) || flags.Interacting)
            return new ReductionResult(next, flags, TimerAction.Cancel, TimeSpan.Zero);
        if (restart)
            return new ReductionResult(next, flags, TimerAction.Arm, DurationFor(mode, s));
        return new ReductionResult(next, flags, TimerAction.Keep, TimeSpan.Zero);
    }

    /// <summary>
    /// True when two states are indistinguishable to a renderer. Thumbnails are compared by content,
    /// because <see cref="MediaInfo"/> record equality compares byte[] by reference.
    /// </summary>
    public static bool AreEquivalent(IslandState a, IslandState b) =>
        a.Mode == b.Mode
        && a.Suspended == b.Suspended
        && a.Volume == b.Volume
        && a.Notice == b.Notice
        && MediaEquivalent(a.Media, b.Media);

    private static bool MediaEquivalent(MediaInfo? a, MediaInfo? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.Title == b.Title
            && a.Artist == b.Artist
            && a.SourceApp == b.SourceApp
            && a.IsPlaying == b.IsPlaying
            && a.Position == b.Position
            && a.Duration == b.Duration
            && (a.Thumbnail is null
                ? b.Thumbnail is null
                : b.Thumbnail is not null && a.Thumbnail.AsSpan().SequenceEqual(b.Thumbnail));
    }

    private static void Request(ref IslandMode mode, ref bool restart, IslandMode requested)
    {
        // The Mini pill is sticky: only an explicit user request (shelf, clipboard, customize) wakes it, never a passive event.
        if (mode == IslandMode.Mini && requested is IslandMode.Volume or IslandMode.MediaPreview or IslandMode.Notice)
        {
            restart = false;
            return;
        }

        mode = EventPriorityPolicy.Resolve(mode, requested);
        // Equal to the requested mode means it was shown (switch) or is already shown (restart).
        restart = mode == requested;
    }

    private static TimeSpan DurationFor(IslandMode mode, IslandSettings s) => mode switch
    {
        IslandMode.Volume => Seconds(s.VolumeDisplaySeconds),
        IslandMode.MediaPreview => Seconds(s.MediaPreviewSeconds),
        IslandMode.Notice => Seconds(s.NoticeSeconds),
        IslandMode.Expanded => Seconds(s.ExpandedIdleSeconds),
        // The clipboard is read more slowly than the shelf, so it stays open twice as long.
        IslandMode.Clipboard => Seconds(s.ExpandedIdleSeconds * 2),
        _ => TimeSpan.Zero,
    };

    // Clamped so a zero or bad setting never produces a zero-delay timer that could fire re-entrantly.
    private static TimeSpan Seconds(double seconds) =>
        double.IsNaN(seconds) ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromSeconds(Math.Clamp(seconds, 0.001, 86_400));
}
