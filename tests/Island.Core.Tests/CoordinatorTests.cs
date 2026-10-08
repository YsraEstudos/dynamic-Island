using Island.Core.Application;
using Island.Core.Configuration;
using Island.Core.Models;

namespace Island.Core.Tests;

public class CoordinatorTests
{
    // ---- Volume ----

    [Fact]
    public void Volume_change_shows_volume_then_returns_to_compact_after_duration()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Volume.SetLevel(40);
        Assert.Equal(IslandMode.Volume, h.Mode);
        Assert.Equal(new VolumeInfo(40, false), h.State.Volume);

        h.Advance(1.7);
        Assert.Equal(IslandMode.Volume, h.Mode);

        h.Advance(0.2);
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(40, h.State.Volume!.Level);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void Repeated_volume_changes_restart_timer_and_keep_the_same_mode()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Volume.SetLevel(10);
        h.Advance(1.0);
        h.Volume.SetLevel(20);
        h.Advance(1.0);
        h.Volume.SetLevel(30);

        // 2 s after the first event, but only 1 s after the last: the timer restarted.
        h.Advance(1.7);
        Assert.Equal(IslandMode.Volume, h.Mode);
        h.Advance(0.2);
        Assert.Equal(IslandMode.Compact, h.Mode);

        // One Compact->Volume transition and one Volume->Compact transition; the middle updates keep Volume.
        Assert.Equal(new[] { IslandMode.Volume, IslandMode.Volume, IslandMode.Volume, IslandMode.Compact }, h.EmittedModes());
        Assert.Equal(new[] { 10, 20, 30 }, h.Emitted.Take(3).Select(s => s.Volume!.Level));
        Assert.Equal(1, h.Scheduler.FiredCount);
    }

    [Fact]
    public void Volume_event_with_unchanged_value_restarts_timer_without_emitting()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Volume.SetLevel(10);
        h.Advance(1.5);
        h.Volume.SetLevel(10);
        h.Advance(1.5);
        Assert.Equal(IslandMode.Volume, h.Mode);
        Assert.Single(h.Emitted);
    }

    [Fact]
    public void Volume_is_ignored_for_display_when_ShowVolume_is_off_but_is_still_stored()
    {
        using var h = new CoordinatorHarness { Settings = new IslandSettings { ShowVolume = false } };
        h.Start();

        h.Volume.SetLevel(15);
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(15, h.State.Volume!.Level);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void Settings_are_read_at_event_time()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Settings = h.Settings with { ShowVolume = false };
        h.Volume.SetLevel(10);
        Assert.Equal(IslandMode.Compact, h.Mode);

        h.Settings = h.Settings with { ShowVolume = true };
        h.Volume.SetLevel(11);
        Assert.Equal(IslandMode.Volume, h.Mode);
    }

    [Fact]
    public void Start_seeds_volume_from_the_service()
    {
        using var h = new CoordinatorHarness();
        h.Volume.SetLevel(77);
        h.Start();

        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(77, h.State.Volume!.Level);
    }

    // ---- Media preview and playback ----

    [Fact]
    public void Track_change_shows_media_preview_then_returns_to_compact()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        Assert.Equal(IslandMode.MediaPreview, h.Mode);
        Assert.Equal("A", h.State.Media!.Title);

        h.Advance(3.4);
        Assert.Equal(IslandMode.MediaPreview, h.Mode);
        h.Advance(0.2);
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Paused_track_change_does_not_show_preview()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A", playing: false));
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal("A", h.State.Media!.Title);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void New_track_during_preview_restarts_preview_duration()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Advance(3.0);
        h.Media.SetMedia(CoordinatorHarness.Track("B"));
        h.Advance(3.4);
        Assert.Equal(IslandMode.MediaPreview, h.Mode);
        Assert.Equal("B", h.State.Media!.Title);
        h.Advance(0.2);
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Playback_tick_updates_media_without_changing_mode_or_restarting_timer()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Advance(2.0);
        h.Media.Tick(TimeSpan.FromSeconds(1));

        Assert.Equal(IslandMode.MediaPreview, h.Mode);
        Assert.Equal(TimeSpan.FromSeconds(1), h.State.Media!.Position);
        Assert.Equal(1, h.Scheduler.PendingCount);

        // The preview deadline is still t = 3.5 s, not t = 5.5 s, so it expires now.
        h.Advance(1.6);
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.All(h.EmittedModes().Take(h.Emitted.Count - 1), m => Assert.Equal(IslandMode.MediaPreview, m));
    }

    [Fact]
    public void Playback_ticks_in_compact_do_not_arm_a_timer()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A", playing: false));
        for (var i = 0; i < 5; i++) h.Media.Tick(TimeSpan.FromSeconds(1));

        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);
        Assert.Equal(TimeSpan.FromSeconds(5), h.State.Media!.Position);
    }

    [Fact]
    public void Same_track_with_identical_content_does_not_emit_again()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A", playing: false, thumbnail: new byte[] { 1, 2, 3 }));
        var count = h.Emitted.Count;

        // Same content, different array instance: must count as the same state.
        h.Media.SetMedia(CoordinatorHarness.Track("A", playing: false, thumbnail: new byte[] { 1, 2, 3 }));
        Assert.Equal(count, h.Emitted.Count);
    }

    [Fact]
    public void Track_change_after_media_removed_is_a_track_change_again()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Advance(4);
        h.Media.SetMedia(null);
        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        Assert.Equal(IslandMode.MediaPreview, h.Mode);
    }

    [Fact]
    public void Start_with_media_already_playing_does_not_show_preview_for_that_track()
    {
        using var h = new CoordinatorHarness();
        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Start();

        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal("A", h.State.Media!.Title);

        h.Media.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(IslandMode.Compact, h.Mode);

        h.Media.SetMedia(CoordinatorHarness.Track("B"));
        Assert.Equal(IslandMode.MediaPreview, h.Mode);
    }

    [Fact]
    public async Task Next_track_from_the_fake_session_shows_preview()
    {
        using var h = new CoordinatorHarness();
        h.Media.SetPlaylist(CoordinatorHarness.Track("A"), CoordinatorHarness.Track("B"));
        h.Start();

        await h.Media.NextAsync();
        Assert.Equal(IslandMode.MediaPreview, h.Mode);
        Assert.Equal("B", h.State.Media!.Title);
    }

    // ---- Priority ----

    [Fact]
    public void Volume_beats_media_preview_and_preview_is_dropped_after_volume()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Volume.SetLevel(10);
        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        Assert.Equal(IslandMode.Volume, h.Mode);
        Assert.Equal("A", h.State.Media!.Title);

        // The preview never resumes after the volume overlay.
        h.Advance(1.8);
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Volume_during_preview_switches_to_volume()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Advance(1.0);
        h.Volume.SetLevel(10);
        Assert.Equal(IslandMode.Volume, h.Mode);

        h.Advance(1.8);
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Media_preview_does_not_beat_expanded()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Post(new IslandEvent.ExpandRequested());
        h.Media.SetMedia(CoordinatorHarness.Track("B"));

        Assert.Equal(IslandMode.Expanded, h.Mode);
        Assert.Equal("B", h.State.Media!.Title);
    }

    [Fact]
    public void Volume_does_not_beat_expanded_but_is_stored()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Post(new IslandEvent.ExpandRequested());
        h.Volume.SetLevel(70);

        Assert.Equal(IslandMode.Expanded, h.Mode);
        Assert.Equal(70, h.State.Volume!.Level);
    }

    // ---- Expanded, interaction and idle collapse ----

    [Fact]
    public void Expanded_auto_collapses_after_idle_seconds()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Post(new IslandEvent.ExpandRequested());
        h.Advance(5.9);
        Assert.Equal(IslandMode.Expanded, h.Mode);
        h.Advance(0.2);
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Expanded_is_preserved_during_interaction_and_idle_timer_restarts_in_full_after()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Post(new IslandEvent.ExpandRequested());
        h.Advance(4.0);

        h.Post(new IslandEvent.InteractionChanged(true));
        Assert.Equal(0, h.Scheduler.PendingCount);
        h.Advance(100);
        Assert.Equal(IslandMode.Expanded, h.Mode);

        // Volume during interaction updates the stored value but does not switch the mode.
        h.Volume.SetLevel(33);
        Assert.Equal(IslandMode.Expanded, h.Mode);
        Assert.Equal(33, h.State.Volume!.Level);

        h.Post(new IslandEvent.InteractionChanged(false));
        Assert.Equal(IslandMode.Expanded, h.Mode);

        // Full duration again, not the 2 s that were left before the interaction.
        h.Advance(5.9);
        Assert.Equal(IslandMode.Expanded, h.Mode);
        h.Advance(0.2);
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Temporary_volume_state_is_held_open_during_interaction()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Volume.SetLevel(10);
        h.Post(new IslandEvent.InteractionChanged(true));
        h.Advance(10);
        Assert.Equal(IslandMode.Volume, h.Mode);

        h.Post(new IslandEvent.InteractionChanged(false));
        h.Advance(1.7);
        Assert.Equal(IslandMode.Volume, h.Mode);
        h.Advance(0.2);
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Expand_requested_while_interacting_starts_idle_timer_only_when_interaction_ends()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Post(new IslandEvent.InteractionChanged(true));
        h.Post(new IslandEvent.ExpandRequested());
        Assert.Equal(IslandMode.Expanded, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);

        h.Advance(60);
        Assert.Equal(IslandMode.Expanded, h.Mode);

        h.Post(new IslandEvent.InteractionChanged(false));
        h.Advance(6.0);
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Expand_opens_the_shelf_without_media_and_auto_collapses()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.ExpandRequested());
        Assert.Equal(IslandMode.Expanded, h.Mode);
        Assert.Equal(1, h.Scheduler.PendingCount);

        h.Advance(6.0);
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Collapse_returns_expanded_to_compact_and_cancels_timer()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Post(new IslandEvent.ExpandRequested());
        h.Post(new IslandEvent.CollapseRequested());

        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void Removing_media_clears_preview_but_keeps_shelf_open()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Media.SetMedia(null);
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);

        h.Media.SetMedia(CoordinatorHarness.Track("B"));
        h.Post(new IslandEvent.ExpandRequested());
        h.Media.SetMedia(null);
        Assert.Equal(IslandMode.Expanded, h.Mode);
        Assert.Null(h.State.Media);
        Assert.Equal(1, h.Scheduler.PendingCount);
    }

    [Fact]
    public void Removing_media_during_volume_keeps_volume_on_screen()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Volume.SetLevel(10);
        h.Media.SetMedia(null);
        Assert.Equal(IslandMode.Volume, h.Mode);
    }

    // ---- Fullscreen and pause ----

    [Fact]
    public void Fullscreen_suspends_collapses_and_resumes_on_exit()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Media.SetMedia(CoordinatorHarness.Track("A"));
        h.Post(new IslandEvent.ExpandRequested());

        h.Display.SetFullscreen(true);
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.True(h.State.Suspended);
        Assert.Equal(0, h.Scheduler.PendingCount);

        // While suspended, data is stored but the mode never changes.
        h.Volume.SetLevel(10);
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(10, h.State.Volume!.Level);

        h.Media.SetMedia(CoordinatorHarness.Track("B"));
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal("B", h.State.Media!.Title);

        h.Post(new IslandEvent.ExpandRequested());
        Assert.Equal(IslandMode.Compact, h.Mode);

        h.Display.SetFullscreen(false);
        Assert.False(h.State.Suspended);
        Assert.Equal(IslandMode.Compact, h.Mode);

        h.Volume.SetLevel(11);
        Assert.Equal(IslandMode.Volume, h.Mode);
    }

    [Fact]
    public void Fullscreen_does_not_suspend_when_HideInFullscreen_is_off()
    {
        using var h = new CoordinatorHarness { Settings = new IslandSettings { HideInFullscreen = false } };
        h.Start();

        h.Display.SetFullscreen(true);
        Assert.False(h.State.Suspended);

        h.Volume.SetLevel(10);
        Assert.Equal(IslandMode.Volume, h.Mode);
    }

    [Fact]
    public void Paused_suspends_and_combines_with_fullscreen()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Post(new IslandEvent.PausedChanged(true));
        Assert.True(h.State.Suspended);

        h.Volume.SetLevel(10);
        Assert.Equal(IslandMode.Compact, h.Mode);

        // Fullscreen exiting must not lift a pause, and vice versa.
        h.Display.SetFullscreen(true);
        h.Display.SetFullscreen(false);
        Assert.True(h.State.Suspended);

        h.Post(new IslandEvent.PausedChanged(false));
        Assert.False(h.State.Suspended);

        h.Display.SetFullscreen(true);
        h.Post(new IslandEvent.PausedChanged(true));
        h.Post(new IslandEvent.PausedChanged(false));
        Assert.True(h.State.Suspended);
    }

    [Fact]
    public void Start_while_fullscreen_seeds_suspended_state()
    {
        using var h = new CoordinatorHarness();
        h.Display.SetFullscreen(true);
        h.Start();

        Assert.True(h.State.Suspended);
    }

    // ---- Timer ownership and stale callbacks ----

    [Fact]
    public void Stale_timer_callback_is_ignored()
    {
        var scheduler = new RecordingScheduler();
        using var h = new CoordinatorHarness(scheduler);
        h.Start();

        h.Volume.SetLevel(1);
        h.Volume.SetLevel(2);
        Assert.Equal(2, scheduler.All.Count);
        var stale = scheduler.All[0];
        var live = scheduler.All[1];
        Assert.True(stale.Disposed);
        Assert.False(live.Disposed);

        // Simulate a callback that was already dispatched before its handle was disposed.
        stale.Callback();
        Assert.Equal(IslandMode.Volume, h.Mode);
        Assert.Equal(2, h.State.Volume!.Level);
        Assert.Equal(2, h.Emitted.Count);

        live.Callback();
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Five_hundred_rapid_volume_events_leave_exactly_one_live_timer()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        for (var i = 0; i < 500; i++)
            h.Volume.SetLevel(i % 100);

        Assert.Equal(1, h.Scheduler.PendingCount);
        Assert.Equal(IslandMode.Volume, h.Mode);

        h.Advance(1.7);
        Assert.Equal(IslandMode.Volume, h.Mode);
        h.Advance(0.2);

        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(1, h.Scheduler.FiredCount);
        Assert.Equal(0, h.Scheduler.PendingCount);
        // Exactly one Volume run and one return to Compact, no matter how many events arrived.
        Assert.Equal(new[] { IslandMode.Volume, IslandMode.Compact }, h.EmittedModes().Distinct());
    }

    [Fact]
    public void Start_raises_the_seeded_state_once_when_it_differs_from_initial()
    {
        using var h = new CoordinatorHarness();
        h.Volume.SetLevel(77);
        h.Coordinator.Start();

        var single = Assert.Single(h.Emitted);
        Assert.Equal(77, single.Volume!.Level);
        Assert.Equal(IslandMode.Compact, single.Mode);
    }

    [Fact]
    public void Temporary_expiry_posted_from_outside_is_ignored()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Volume.SetLevel(10);
        h.Post(new IslandEvent.TemporaryStateExpired());
        Assert.Equal(IslandMode.Volume, h.Mode);
        Assert.Equal(1, h.Scheduler.PendingCount);
    }

    [Fact]
    public void Dispose_cancels_timer_and_ignores_later_events()
    {
        var h = new CoordinatorHarness();
        h.Start();

        h.Volume.SetLevel(5);
        Assert.Equal(1, h.Scheduler.PendingCount);

        h.Coordinator.Dispose();
        Assert.Equal(0, h.Scheduler.PendingCount);

        h.Volume.SetLevel(6);
        h.Post(new IslandEvent.CollapseRequested());
        Assert.Equal(IslandMode.Volume, h.Mode);
        Assert.Equal(5, h.State.Volume!.Level);
        Assert.Single(h.Emitted);
    }

    // ---- Emission rules and concurrency ----

    [Fact]
    public void Equal_state_is_not_raised_again()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        h.Volume.SetLevel(10);
        h.Volume.SetLevel(10);
        Assert.Single(h.Emitted, s => s.Mode == IslandMode.Volume);
    }

    [Fact]
    public void Handler_that_posts_during_emission_is_queued_and_order_is_preserved()
    {
        using var h = new CoordinatorHarness();
        var posted = false;
        h.Coordinator.StateChanged += state =>
        {
            if (state.Mode == IslandMode.Volume && !posted)
            {
                posted = true;
                h.Post(new IslandEvent.CollapseRequested());
            }
        };
        h.Start();

        h.Volume.SetLevel(10);

        Assert.Equal(new[] { IslandMode.Volume, IslandMode.Compact }, h.EmittedModes());
        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Concurrent_events_keep_one_timer_and_serialize_emissions()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        Parallel.For(0, 8, t =>
        {
            for (var i = 0; i < 200; i++)
                h.Volume.SetLevel((t * 200 + i) % 100);
        });

        Assert.Equal(IslandMode.Volume, h.Mode);
        Assert.Equal(1, h.Scheduler.PendingCount);
        // Emissions were appended under the coordinator's serialization, so the last one matches the live state.
        Assert.Equal(h.State, h.Emitted[^1]);
    }
}
