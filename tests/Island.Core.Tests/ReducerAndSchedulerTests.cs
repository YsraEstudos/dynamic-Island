using Island.Core.Application;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Models;

namespace Island.Core.Tests;

public class ReducerTests
{
    private static readonly IslandSettings Settings = new();
    private static readonly ReducerFlags NoFlags = ReducerFlags.None;

    private static MediaInfo Playing(string title) =>
        new(title, "Artist", null, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), "Spotify");

    [Theory]
    [InlineData(IslandMode.Compact, IslandMode.Volume, IslandMode.Volume)]
    [InlineData(IslandMode.MediaPreview, IslandMode.Volume, IslandMode.Volume)]
    [InlineData(IslandMode.Volume, IslandMode.MediaPreview, IslandMode.Volume)]
    [InlineData(IslandMode.Expanded, IslandMode.Volume, IslandMode.Expanded)]
    [InlineData(IslandMode.Expanded, IslandMode.MediaPreview, IslandMode.Expanded)]
    [InlineData(IslandMode.Volume, IslandMode.Volume, IslandMode.Volume)]
    [InlineData(IslandMode.Compact, IslandMode.Compact, IslandMode.Compact)]
    public void Priority_resolves_to_the_higher_ranked_mode(IslandMode current, IslandMode requested, IslandMode expected)
    {
        Assert.Equal(expected, EventPriorityPolicy.Resolve(current, requested));
    }

    [Fact]
    public void Volume_from_compact_arms_timer_with_volume_duration()
    {
        var result = IslandStateReducer.Reduce(IslandState.Initial, NoFlags,
            new IslandEvent.VolumeChanged(new VolumeInfo(20, false)), Settings);

        Assert.Equal(IslandMode.Volume, result.State.Mode);
        Assert.Equal(TimerAction.Arm, result.Timer);
        Assert.Equal(TimeSpan.FromSeconds(Settings.VolumeDisplaySeconds), result.Delay);
    }

    [Fact]
    public void Silent_volume_update_stores_the_value_without_showing_the_indicator()
    {
        var result = IslandStateReducer.Reduce(IslandState.Initial, NoFlags,
            new IslandEvent.VolumeChanged(new VolumeInfo(80, false), Silent: true), Settings);

        Assert.Equal(IslandMode.Compact, result.State.Mode);
        Assert.Equal(new VolumeInfo(80, false), result.State.Volume);
    }

    [Fact]
    public void Playback_tick_keeps_timer_and_mode()
    {
        var preview = IslandStateReducer.Reduce(IslandState.Initial, NoFlags,
            new IslandEvent.MediaChanged(Playing("A"), TrackChanged: true), Settings);
        var tick = IslandStateReducer.Reduce(preview.State, preview.Flags,
            new IslandEvent.MediaChanged(Playing("A"), TrackChanged: false), Settings);

        Assert.Equal(TimerAction.Keep, tick.Timer);
        Assert.Equal(IslandMode.MediaPreview, tick.State.Mode);
    }

    [Fact]
    public void Volume_while_interacting_cancels_rather_than_arming()
    {
        var result = IslandStateReducer.Reduce(IslandState.Initial, new ReducerFlags(false, false, true),
            new IslandEvent.VolumeChanged(new VolumeInfo(20, false)), Settings);

        Assert.Equal(IslandMode.Volume, result.State.Mode);
        Assert.Equal(TimerAction.Cancel, result.Timer);
    }

    [Fact]
    public void Ending_interaction_in_expanded_arms_no_timer()
    {
        var expanded = IslandStateReducer.Reduce(
            IslandState.Initial with { Media = Playing("A") },
            new ReducerFlags(false, false, true),
            new IslandEvent.ExpandRequested(), Settings);
        var resumed = IslandStateReducer.Reduce(expanded.State, expanded.Flags,
            new IslandEvent.InteractionChanged(false), Settings);

        Assert.Equal(TimerAction.Cancel, expanded.Timer);
        Assert.Equal(TimerAction.Cancel, resumed.Timer);
        Assert.Equal(IslandMode.Expanded, resumed.State.Mode);
    }

    [Fact]
    public void Suspension_is_paused_or_fullscreen_with_hide_setting()
    {
        Assert.True(IslandStateReducer.IsSuspended(new ReducerFlags(false, true, false), Settings));
        Assert.True(IslandStateReducer.IsSuspended(new ReducerFlags(true, false, false), Settings));
        Assert.False(IslandStateReducer.IsSuspended(new ReducerFlags(true, false, false),
            Settings with { HideInFullscreen = false }));
    }

    [Fact]
    public void Equivalence_compares_thumbnails_by_content()
    {
        var a = IslandState.Initial with { Media = Playing("A") with { Thumbnail = new byte[] { 9, 9 } } };
        var b = IslandState.Initial with { Media = Playing("A") with { Thumbnail = new byte[] { 9, 9 } } };
        var c = IslandState.Initial with { Media = Playing("A") with { Thumbnail = new byte[] { 9, 8 } } };

        Assert.True(IslandStateReducer.AreEquivalent(a, b));
        Assert.False(IslandStateReducer.AreEquivalent(a, c));
        Assert.False(IslandStateReducer.AreEquivalent(a, IslandState.Initial));
    }
}

public class ManualSchedulerTests
{
    [Fact]
    public void Advance_fires_due_callbacks_in_order_and_skips_cancelled_ones()
    {
        var scheduler = new ManualScheduler();
        var fired = new List<string>();

        scheduler.Schedule(TimeSpan.FromSeconds(2), () => fired.Add("two"));
        var cancelled = scheduler.Schedule(TimeSpan.FromSeconds(1), () => fired.Add("cancelled"));
        scheduler.Schedule(TimeSpan.FromSeconds(1), () => fired.Add("one"));
        cancelled.Dispose();

        Assert.Equal(2, scheduler.PendingCount);
        scheduler.Advance(TimeSpan.FromSeconds(1.5));
        Assert.Equal(new[] { "one" }, fired);

        scheduler.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { "one", "two" }, fired);
        Assert.Equal(0, scheduler.PendingCount);
        Assert.Equal(2, scheduler.FiredCount);
    }

    [Fact]
    public void Callback_scheduled_during_advance_is_relative_to_its_own_due_time()
    {
        var scheduler = new ManualScheduler();
        TimeSpan? secondFiredAt = null;

        scheduler.Schedule(TimeSpan.FromSeconds(1), () =>
            scheduler.Schedule(TimeSpan.FromSeconds(1), () => secondFiredAt = scheduler.Now));

        scheduler.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(2), secondFiredAt);
    }

    [Fact]
    public void Disposing_a_handle_twice_is_harmless()
    {
        var scheduler = new ManualScheduler();
        var handle = scheduler.Schedule(TimeSpan.FromSeconds(1), () => { });
        handle.Dispose();
        handle.Dispose();
        Assert.Equal(0, scheduler.PendingCount);
    }
}

public class SystemIslandSchedulerTests
{
    [Fact]
    public void Scheduled_callback_runs_once_on_the_timer()
    {
        var scheduler = new SystemIslandScheduler();
        var count = 0;
        using var fired = new ManualResetEventSlim(false);

        using var handle = scheduler.Schedule(TimeSpan.FromMilliseconds(20), () =>
        {
            Interlocked.Increment(ref count);
            fired.Set();
        });

        Assert.True(fired.Wait(TimeSpan.FromSeconds(5)));
        Thread.Sleep(100);
        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public void Disposing_before_the_delay_cancels_the_callback()
    {
        var scheduler = new SystemIslandScheduler();
        var count = 0;

        var handle = scheduler.Schedule(TimeSpan.FromMilliseconds(100), () => Interlocked.Increment(ref count));
        handle.Dispose();

        Thread.Sleep(300);
        Assert.Equal(0, Volatile.Read(ref count));
    }
}
