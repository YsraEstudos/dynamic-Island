using Island.Core.Application;
using Island.Core.Configuration;
using Island.Core.Models;

namespace Island.Core.Tests;

/// <summary>Customize, Clipboard, Notice and the Expanded-without-media rule, at reducer and coordinator level.</summary>
public class ShelfModesTests
{
    private static readonly IslandSettings Settings = new();

    private static readonly Notice Done = new("Focus done", "Take a break", "timer");
    private static readonly Notice Copied = new("Copied");

    private static IslandState In(IslandMode mode, Notice? notice = null) =>
        IslandState.Initial with { Mode = mode, Notice = notice };

    private static MediaInfo Playing(string title) =>
        new(title, "Artist", null, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), "Spotify");

    private static ReductionResult Reduce(IslandState state, IslandEvent e, IslandSettings? s = null, ReducerFlags? flags = null) =>
        IslandStateReducer.Reduce(state, flags ?? ReducerFlags.None, e, s ?? Settings);

    // ---- Priority table (EventPriorityPolicy.Resolve) ----

    [Theory]
    // Customize is the highest priority.
    [InlineData(IslandMode.Expanded, IslandMode.Customize, IslandMode.Customize)]
    [InlineData(IslandMode.Clipboard, IslandMode.Customize, IslandMode.Customize)]
    [InlineData(IslandMode.Notice, IslandMode.Customize, IslandMode.Customize)]
    [InlineData(IslandMode.Volume, IslandMode.Customize, IslandMode.Customize)]
    [InlineData(IslandMode.Customize, IslandMode.Clipboard, IslandMode.Customize)]
    [InlineData(IslandMode.Customize, IslandMode.Volume, IslandMode.Customize)]
    [InlineData(IslandMode.Customize, IslandMode.MediaPreview, IslandMode.Customize)]
    [InlineData(IslandMode.Customize, IslandMode.Notice, IslandMode.Customize)]
    // Clipboard and Expanded are protected from the lower modes, and equal to each other.
    [InlineData(IslandMode.Clipboard, IslandMode.Volume, IslandMode.Clipboard)]
    [InlineData(IslandMode.Clipboard, IslandMode.Notice, IslandMode.Clipboard)]
    [InlineData(IslandMode.Clipboard, IslandMode.MediaPreview, IslandMode.Clipboard)]
    [InlineData(IslandMode.Expanded, IslandMode.Clipboard, IslandMode.Clipboard)]
    [InlineData(IslandMode.Clipboard, IslandMode.Expanded, IslandMode.Expanded)]
    [InlineData(IslandMode.Expanded, IslandMode.Notice, IslandMode.Expanded)]
    [InlineData(IslandMode.Expanded, IslandMode.Volume, IslandMode.Expanded)]
    [InlineData(IslandMode.Expanded, IslandMode.MediaPreview, IslandMode.Expanded)]
    // Notice and Volume: latest wins.
    [InlineData(IslandMode.Volume, IslandMode.Notice, IslandMode.Notice)]
    [InlineData(IslandMode.Notice, IslandMode.Volume, IslandMode.Volume)]
    [InlineData(IslandMode.Notice, IslandMode.Notice, IslandMode.Notice)]
    [InlineData(IslandMode.Notice, IslandMode.MediaPreview, IslandMode.Notice)]
    [InlineData(IslandMode.MediaPreview, IslandMode.Notice, IslandMode.Notice)]
    // Compact is the lowest.
    [InlineData(IslandMode.Compact, IslandMode.Clipboard, IslandMode.Clipboard)]
    [InlineData(IslandMode.Compact, IslandMode.Notice, IslandMode.Notice)]
    [InlineData(IslandMode.Compact, IslandMode.Customize, IslandMode.Customize)]
    public void Priority_table_resolves_to_the_expected_mode(IslandMode current, IslandMode requested, IslandMode expected)
    {
        Assert.Equal(expected, EventPriorityPolicy.Resolve(current, requested));
    }

    // ---- Reducer: Expanded, Customize, Clipboard ----

    [Fact]
    public void Expand_without_media_opens_the_shelf_without_a_timer()
    {
        var result = Reduce(IslandState.Initial, new IslandEvent.ExpandRequested());

        Assert.Equal(IslandMode.Expanded, result.State.Mode);
        Assert.Equal(TimerAction.Cancel, result.Timer);
    }

    [Theory]
    [InlineData(IslandMode.Customize)]
    [InlineData(IslandMode.Clipboard)]
    [InlineData(IslandMode.Volume)]
    [InlineData(IslandMode.Notice)]
    public void Expand_always_goes_to_expanded_including_out_of_customize_and_clipboard(IslandMode from)
    {
        var result = Reduce(In(from), new IslandEvent.ExpandRequested());

        Assert.Equal(IslandMode.Expanded, result.State.Mode);
        Assert.Equal(TimerAction.Cancel, result.Timer);
    }

    [Fact]
    public void Expand_while_suspended_stays_compact()
    {
        var result = Reduce(IslandState.Initial, new IslandEvent.ExpandRequested(), flags: new ReducerFlags(false, true, false));

        Assert.Equal(IslandMode.Compact, result.State.Mode);
        Assert.Equal(TimerAction.Cancel, result.Timer);
    }

    [Fact]
    public void Customize_has_no_timer_from_compact_or_from_a_timed_mode()
    {
        var fromCompact = Reduce(IslandState.Initial, new IslandEvent.CustomizeRequested());
        Assert.Equal(IslandMode.Customize, fromCompact.State.Mode);
        Assert.Equal(TimerAction.Cancel, fromCompact.Timer);

        var fromVolume = Reduce(In(IslandMode.Volume), new IslandEvent.CustomizeRequested());
        Assert.Equal(IslandMode.Customize, fromVolume.State.Mode);
        Assert.Equal(TimerAction.Cancel, fromVolume.Timer);
    }

    [Fact]
    public void Temporary_expiry_does_not_leave_customize()
    {
        var result = Reduce(In(IslandMode.Customize), new IslandEvent.TemporaryStateExpired());

        Assert.Equal(IslandMode.Customize, result.State.Mode);
    }

    [Fact]
    public void Clipboard_request_opens_the_clipboard_without_a_timer()
    {
        var result = Reduce(IslandState.Initial, new IslandEvent.ClipboardRequested());

        Assert.Equal(IslandMode.Clipboard, result.State.Mode);
        Assert.Equal(TimerAction.Cancel, result.Timer);
    }

    [Fact]
    public void Clipboard_request_is_ignored_when_disabled()
    {
        var result = Reduce(IslandState.Initial, new IslandEvent.ClipboardRequested(),
            Settings with { ClipboardEnabled = false });

        Assert.Equal(IslandMode.Compact, result.State.Mode);
        Assert.Equal(TimerAction.Cancel, result.Timer);
    }

    [Fact]
    public void Clipboard_request_does_not_displace_customize()
    {
        var result = Reduce(In(IslandMode.Customize), new IslandEvent.ClipboardRequested());

        Assert.Equal(IslandMode.Customize, result.State.Mode);
    }

    [Fact]
    public void Clipboard_request_from_expanded_switches_without_a_timer()
    {
        var result = Reduce(In(IslandMode.Expanded), new IslandEvent.ClipboardRequested());

        Assert.Equal(IslandMode.Clipboard, result.State.Mode);
        Assert.Equal(TimerAction.Cancel, result.Timer);
    }

    [Theory]
    [InlineData(IslandMode.Expanded)]
    [InlineData(IslandMode.Clipboard)]
    public void Temporary_expiry_does_not_close_the_shelf_or_clipboard(IslandMode open)
    {
        var result = Reduce(In(open), new IslandEvent.TemporaryStateExpired());

        Assert.Equal(open, result.State.Mode);
    }

    // ---- Reducer: Notice ----

    [Fact]
    public void Notice_from_compact_shows_the_notice_and_arms_the_notice_duration()
    {
        var result = Reduce(IslandState.Initial, new IslandEvent.NoticeRaised(Done));

        Assert.Equal(IslandMode.Notice, result.State.Mode);
        Assert.Equal(Done, result.State.Notice);
        Assert.Equal(TimerAction.Arm, result.Timer);
        Assert.Equal(TimeSpan.FromSeconds(Settings.NoticeSeconds), result.Delay);
    }

    [Theory]
    [InlineData(IslandMode.Expanded)]
    [InlineData(IslandMode.Clipboard)]
    [InlineData(IslandMode.Customize)]
    public void Notice_is_dropped_while_a_protected_mode_is_shown(IslandMode protectedMode)
    {
        var result = Reduce(In(protectedMode), new IslandEvent.NoticeRaised(Done));

        Assert.Equal(protectedMode, result.State.Mode);
        Assert.Null(result.State.Notice);
    }

    [Fact]
    public void Notice_replaces_a_shown_notice_and_restarts_the_timer()
    {
        var result = Reduce(In(IslandMode.Notice, Done), new IslandEvent.NoticeRaised(Copied));

        Assert.Equal(IslandMode.Notice, result.State.Mode);
        Assert.Equal(Copied, result.State.Notice);
        Assert.Equal(TimerAction.Arm, result.Timer);
        Assert.Equal(TimeSpan.FromSeconds(Settings.NoticeSeconds), result.Delay);
    }

    [Fact]
    public void Notice_over_media_preview_switches_to_notice()
    {
        var preview = Reduce(IslandState.Initial, new IslandEvent.MediaChanged(Playing("A"), TrackChanged: true));
        var result = Reduce(preview.State, new IslandEvent.NoticeRaised(Done));

        Assert.Equal(IslandMode.Notice, result.State.Mode);
    }

    [Fact]
    public void Volume_replaces_a_notice_and_clears_its_content()
    {
        var result = Reduce(In(IslandMode.Notice, Done), new IslandEvent.VolumeChanged(new VolumeInfo(30, false)));

        Assert.Equal(IslandMode.Volume, result.State.Mode);
        Assert.Null(result.State.Notice);
        Assert.Equal(TimerAction.Arm, result.Timer);
    }

    [Fact]
    public void Media_preview_does_not_displace_a_notice_and_keeps_it()
    {
        var result = Reduce(In(IslandMode.Notice, Done), new IslandEvent.MediaChanged(Playing("B"), TrackChanged: true));

        Assert.Equal(IslandMode.Notice, result.State.Mode);
        Assert.Equal(Done, result.State.Notice);
        Assert.Equal("B", result.State.Media!.Title);
    }

    [Fact]
    public void Notice_expiry_returns_to_compact_and_clears_the_notice()
    {
        var result = Reduce(In(IslandMode.Notice, Done), new IslandEvent.TemporaryStateExpired());

        Assert.Equal(IslandMode.Compact, result.State.Mode);
        Assert.Null(result.State.Notice);
        Assert.Equal(TimerAction.Cancel, result.Timer);
    }

    [Fact]
    public void Collapse_clears_the_notice()
    {
        var result = Reduce(In(IslandMode.Notice, Done), new IslandEvent.CollapseRequested());

        Assert.Equal(IslandMode.Compact, result.State.Mode);
        Assert.Null(result.State.Notice);
    }

    [Fact]
    public void Notice_is_suspended_like_everything_else_and_loses_its_content()
    {
        var result = Reduce(IslandState.Initial, new IslandEvent.NoticeRaised(Done),
            flags: new ReducerFlags(false, true, false));

        Assert.Equal(IslandMode.Compact, result.State.Mode);
        Assert.Null(result.State.Notice);
        Assert.True(result.State.Suspended);
    }

    [Fact]
    public void Notice_while_interacting_is_held_and_restarts_in_full_after_interaction()
    {
        var shown = Reduce(IslandState.Initial, new IslandEvent.NoticeRaised(Done));
        var held = Reduce(shown.State, new IslandEvent.InteractionChanged(true), flags: shown.Flags);
        Assert.Equal(TimerAction.Cancel, held.Timer);
        Assert.Equal(IslandMode.Notice, held.State.Mode);

        var resumed = Reduce(held.State, new IslandEvent.InteractionChanged(false), flags: held.Flags);
        Assert.Equal(TimerAction.Arm, resumed.Timer);
        Assert.Equal(TimeSpan.FromSeconds(Settings.NoticeSeconds), resumed.Delay);
    }

    [Fact]
    public void Notice_is_compared_by_content_for_emission()
    {
        var a = In(IslandMode.Notice, new Notice("X"));
        var same = In(IslandMode.Notice, new Notice("X"));
        var other = In(IslandMode.Notice, new Notice("Y"));

        Assert.True(IslandStateReducer.AreEquivalent(a, same));
        Assert.False(IslandStateReducer.AreEquivalent(a, other));
    }

    // ---- Reducer: timer invariant over all modes ----

    [Theory]
    [InlineData(IslandMode.Compact, false)]
    [InlineData(IslandMode.Volume, true)]
    [InlineData(IslandMode.MediaPreview, true)]
    [InlineData(IslandMode.Expanded, false)]
    [InlineData(IslandMode.Clipboard, false)]
    [InlineData(IslandMode.Notice, true)]
    [InlineData(IslandMode.Customize, false)]
    public void Only_timer_owned_modes_have_a_timer(IslandMode mode, bool hasTimer)
    {
        Assert.Equal(hasTimer, EventPriorityPolicy.HasTimer(mode));
    }

    // ---- Coordinator ----

    [Fact]
    public void Notice_shows_then_returns_to_compact_after_notice_seconds()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.NoticeRaised(Done));
        Assert.Equal(IslandMode.Notice, h.Mode);
        Assert.Equal(Done, h.State.Notice);

        h.Advance(2.9);
        Assert.Equal(IslandMode.Notice, h.Mode);
        h.Advance(0.2);
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Null(h.State.Notice);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void Notice_duration_follows_settings()
    {
        using var h = new CoordinatorHarness { Settings = new IslandSettings { NoticeSeconds = 1.5 } };
        h.Start();

        h.Post(new IslandEvent.NoticeRaised(Copied));
        h.Advance(1.4);
        Assert.Equal(IslandMode.Notice, h.Mode);
        h.Advance(0.2);
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void A_second_notice_replaces_the_first_and_restarts_the_timer()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.NoticeRaised(Done));
        h.Advance(2.0);
        h.Post(new IslandEvent.NoticeRaised(Copied));
        Assert.Equal(IslandMode.Notice, h.Mode);
        Assert.Equal(Copied, h.State.Notice);
        Assert.Equal(1, h.Scheduler.PendingCount);

        // Expires 3 s after the second notice, i.e. at t = 5 s.
        h.Advance(2.9);
        Assert.Equal(IslandMode.Notice, h.Mode);
        h.Advance(0.2);
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Identical_notice_restarts_the_timer_without_emitting()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.NoticeRaised(Done));
        h.Advance(2.0);
        var count = h.Emitted.Count;
        h.Post(new IslandEvent.NoticeRaised(Done));

        Assert.Equal(count, h.Emitted.Count);
        Assert.Equal(1, h.Scheduler.PendingCount);
        h.Advance(2.9);
        Assert.Equal(IslandMode.Notice, h.Mode);
        h.Advance(0.2);
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Notice_is_dropped_while_expanded_and_the_shelf_has_no_timer()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.ExpandRequested());
        h.Post(new IslandEvent.NoticeRaised(Done));

        Assert.Equal(IslandMode.Expanded, h.Mode);
        Assert.Null(h.State.Notice);
        Assert.Equal(0, h.Scheduler.PendingCount);
        h.Advance(60.0);
        Assert.Equal(IslandMode.Expanded, h.Mode);
    }

    [Fact]
    public void Notice_is_dropped_while_customize_or_clipboard_is_shown()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.CustomizeRequested());
        h.Post(new IslandEvent.NoticeRaised(Done));
        Assert.Equal(IslandMode.Customize, h.Mode);
        Assert.Null(h.State.Notice);

        h.Post(new IslandEvent.CollapseRequested());
        h.Post(new IslandEvent.ClipboardRequested());
        h.Post(new IslandEvent.NoticeRaised(Done));
        Assert.Equal(IslandMode.Clipboard, h.Mode);
        Assert.Null(h.State.Notice);
    }

    [Fact]
    public void Volume_and_notice_are_latest_wins()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.NoticeRaised(Done));
        h.Volume.SetLevel(10);
        Assert.Equal(IslandMode.Volume, h.Mode);
        Assert.Null(h.State.Notice);

        h.Post(new IslandEvent.NoticeRaised(Done));
        Assert.Equal(IslandMode.Notice, h.Mode);
        Assert.Equal(10, h.State.Volume!.Level);
    }

    [Fact]
    public void Notice_raised_while_fullscreen_is_suspended_and_not_shown()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Display.SetFullscreen(true);
        h.Post(new IslandEvent.NoticeRaised(Done));
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Null(h.State.Notice);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void Customize_has_no_idle_timer_and_stays_until_collapse()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.CustomizeRequested());
        Assert.Equal(IslandMode.Customize, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);

        h.Advance(600);
        Assert.Equal(IslandMode.Customize, h.Mode);

        h.Post(new IslandEvent.CollapseRequested());
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Volume_and_media_are_stored_but_do_not_leave_customize()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.CustomizeRequested());
        h.Volume.SetLevel(30);
        h.Media.SetMedia(CoordinatorHarness.Track("A"));

        Assert.Equal(IslandMode.Customize, h.Mode);
        Assert.Equal(30, h.State.Volume!.Level);
        Assert.Equal("A", h.State.Media!.Title);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void Customize_beats_clipboard_and_expanded()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.ExpandRequested());
        h.Post(new IslandEvent.CustomizeRequested());
        Assert.Equal(IslandMode.Customize, h.Mode);

        h.Post(new IslandEvent.ClipboardRequested());
        Assert.Equal(IslandMode.Customize, h.Mode);
    }

    [Fact]
    public void Expand_from_customize_is_the_done_action_and_stays_open()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.CustomizeRequested());
        h.Post(new IslandEvent.ExpandRequested());
        Assert.Equal(IslandMode.Expanded, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);

        h.Advance(600.0);
        Assert.Equal(IslandMode.Expanded, h.Mode);
    }

    [Fact]
    public void Customize_request_is_ignored_while_suspended()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Display.SetFullscreen(true);
        h.Post(new IslandEvent.CustomizeRequested());
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.True(h.State.Suspended);
    }

    [Fact]
    public void Clipboard_stays_open_until_it_is_closed()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.ClipboardRequested());
        Assert.Equal(IslandMode.Clipboard, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);

        h.Advance(600.0);
        Assert.Equal(IslandMode.Clipboard, h.Mode);
    }

    [Fact]
    public void Clipboard_request_is_ignored_when_disabled_in_the_coordinator()
    {
        using var h = new CoordinatorHarness { Settings = new IslandSettings { ClipboardEnabled = false } };
        h.Start();

        h.Post(new IslandEvent.ClipboardRequested());
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void Clipboard_stays_open_through_hover_and_after_it_ends()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.ClipboardRequested());
        h.Post(new IslandEvent.InteractionChanged(true));
        h.Advance(100);
        Assert.Equal(IslandMode.Clipboard, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);

        h.Post(new IslandEvent.InteractionChanged(false));
        h.Advance(100);
        Assert.Equal(IslandMode.Clipboard, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void Clipboard_is_not_displaced_by_media_or_volume()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.ClipboardRequested());
        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Volume.SetLevel(20);

        Assert.Equal(IslandMode.Clipboard, h.Mode);
        Assert.Equal("A", h.State.Media!.Title);
        Assert.Equal(20, h.State.Volume!.Level);
    }

    [Fact]
    public void Back_from_clipboard_to_expanded_and_clipboard_again_round_trips()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.ExpandRequested());
        h.Post(new IslandEvent.ClipboardRequested());
        Assert.Equal(IslandMode.Clipboard, h.Mode);

        h.Post(new IslandEvent.ExpandRequested());
        Assert.Equal(IslandMode.Expanded, h.Mode);
        h.Advance(600.0);
        Assert.Equal(IslandMode.Expanded, h.Mode);
    }

    [Fact]
    public void Removing_media_keeps_the_shelf_open()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Post(new IslandEvent.ExpandRequested());
        h.Media.SetMedia(null);

        Assert.Equal(IslandMode.Expanded, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }
}
