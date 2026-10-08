using System.Collections.Concurrent;
using Island.Core.Application;
using Island.Core.Models;

namespace Island.Core.Tests;

/// <summary>
/// Long event sequences on the manual clock. Nothing here depends on wall-clock time: every timer is driven by Advance.
/// </summary>
public class StressTests
{
    [Fact]
    public void Hundreds_of_volume_events_keep_a_single_pending_timer()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        for (var i = 0; i < 500; i++)
        {
            h.Volume.SetLevel(i % 100);
            h.Advance(0.01);
            Assert.True(h.Scheduler.PendingCount <= 2, $"pending timers after event {i}: {h.Scheduler.PendingCount}");
        }

        // 500 events over 5 s is longer than the 1.8 s overlay: it stays open only because each event restarts the timer.
        Assert.Equal(IslandMode.Volume, h.Mode);
        Assert.Equal(99, h.State.Volume!.Level);

        h.Advance(60);
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void Thousands_of_open_close_cycles_do_not_leak_timers()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        for (var i = 0; i < 3000; i++)
        {
            switch (i % 3)
            {
                case 0: // Volume overlay, left to expire.
                    h.Volume.SetLevel(i % 100);
                    h.Advance(2.0);
                    break;
                case 1: // Media preview, left to expire.
                    h.Media.SetMedia(CoordinatorHarness.Track($"Track {i}"));
                    h.Advance(4.0);
                    break;
                default: // Shelf: expires on even cycles, collapsed explicitly on odd ones.
                    h.Post(new IslandEvent.ExpandRequested());
                    if (i % 2 == 0) h.Advance(7.0);
                    else h.Post(new IslandEvent.CollapseRequested());
                    break;
            }

            Assert.Equal(IslandMode.Compact, h.Mode);
            Assert.Equal(0, h.Scheduler.PendingCount);
        }

        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);
        // 1000 volume expiries + 1000 preview expiries + 500 shelf expiries (even cycles only). Collapses cancel, never fire.
        Assert.Equal(2500, h.Scheduler.FiredCount);
    }

    [Fact]
    public async Task Mixed_event_storm_ends_in_a_consistent_state()
    {
        using var h = new CoordinatorHarness();
        h.Media.SetPlaylist(CoordinatorHarness.Track("P0"), CoordinatorHarness.Track("P1"), CoordinatorHarness.Track("P2"));
        h.Start();
        var rng = new Random(1234);

        for (var i = 0; i < 2000; i++)
        {
            switch (rng.Next(12))
            {
                case 0:
                    h.Volume.SetLevel(rng.Next(0, 101));
                    break;
                case 1:
                    h.Volume.SetMuted(rng.Next(2) == 0);
                    break;
                case 2:
                    h.Media.SetMedia(CoordinatorHarness.Track(
                        $"Song {rng.Next(8)}",
                        playing: rng.Next(3) != 0,
                        thumbnail: rng.Next(2) == 0 ? new[] { (byte)i } : null));
                    break;
                case 3:
                    h.Media.SetMedia(null);
                    break;
                case 4:
                    h.Media.Tick(TimeSpan.FromSeconds(rng.Next(1, 5)));
                    break;
                case 5:
                    await h.Media.NextAsync();
                    break;
                case 6:
                    await h.Media.PlayPauseAsync();
                    break;
                case 7:
                    h.Post(new IslandEvent.NoticeRaised(new Notice($"Notice {i}")));
                    break;
                case 8:
                    h.Post(new IslandEvent.InteractionChanged(rng.Next(2) == 0));
                    break;
                case 9:
                    h.Post(new IslandEvent.PausedChanged(rng.Next(4) == 0));
                    break;
                case 10:
                    h.Display.SetFullscreen(rng.Next(3) == 0);
                    break;
                default:
                    IslandEvent shelf = rng.Next(4) switch
                    {
                        0 => new IslandEvent.ExpandRequested(),
                        1 => new IslandEvent.CollapseRequested(),
                        2 => new IslandEvent.CustomizeRequested(),
                        _ => new IslandEvent.ClipboardRequested(),
                    };
                    h.Post(shelf);
                    break;
            }

            h.Advance(rng.NextDouble() * 1.5);
            // The coordinator owns one timer at most; every arm cancels the previous one.
            Assert.True(h.Scheduler.PendingCount <= 1, $"pending timers after step {i}: {h.Scheduler.PendingCount}");
        }

        // Release any interaction still held open, then let the clock run for ten minutes.
        h.Post(new IslandEvent.InteractionChanged(false));
        h.Advance(600);

        Assert.Equal(0, h.Scheduler.PendingCount);
        // Compact once the timers run out. Customize has no timer by design and stays until Expand or Collapse.
        Assert.True(h.Mode is IslandMode.Compact or IslandMode.Customize, $"unexpected settled mode {h.Mode}");
        if (h.State.Suspended) Assert.Equal(IslandMode.Compact, h.Mode);

        var settled = h.Emitted.Count;
        h.Advance(600);
        Assert.Equal(settled, h.Emitted.Count);

        // Holds by construction: CommitLocked compares each new state with the last committed one before queueing it.
        for (var i = 1; i < h.Emitted.Count; i++)
            Assert.False(IslandStateReducer.AreEquivalent(h.Emitted[i - 1], h.Emitted[i]), $"duplicate emission at index {i}");
    }

    [Fact]
    public void Concurrent_posts_from_many_threads_do_not_throw()
    {
        // Post applies the event under the coordinator's lock and drains emissions one thread at a time, so it is thread-safe.
        using var h = new CoordinatorHarness();
        h.Start();

        var exceptions = new ConcurrentQueue<Exception>();
        var go = new ManualResetEventSlim(false);
        var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
        {
            try
            {
                go.Wait();
                for (var i = 0; i < 500; i++)
                {
                    var n = t * 500 + i;
                    if (n % 2 == 0)
                        h.Post(new IslandEvent.VolumeChanged(new VolumeInfo(n % 101, n % 7 == 0)));
                    else
                        h.Post(new IslandEvent.MediaChanged(CoordinatorHarness.Track($"T{n}"), TrackChanged: n % 3 != 0));
                }
            }
            catch (Exception ex)
            {
                exceptions.Enqueue(ex);
            }
        }) { IsBackground = true }).ToList();

        threads.ForEach(t => t.Start());
        go.Set();
        threads.ForEach(t => t.Join());

        Assert.Empty(exceptions);
        // Only volume and media events were posted, so the settled mode is one of the two timer-owned overlays.
        Assert.True(h.Mode is IslandMode.Volume or IslandMode.MediaPreview, $"unexpected mode {h.Mode}");
        Assert.Equal(1, h.Scheduler.PendingCount);
        // All threads have returned, so every committed state has been drained and the last emission is the live state.
        Assert.True(IslandStateReducer.AreEquivalent(h.State, h.Emitted[^1]));

        h.Advance(60);
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(0, h.Scheduler.PendingCount);
    }

    [Fact]
    public void Dispose_cancels_all_pending_timers()
    {
        using var h = new CoordinatorHarness();
        h.Start();

        for (var i = 0; i < 300; i++)
        {
            switch (i % 3)
            {
                case 0:
                    h.Volume.SetLevel(i % 100);
                    break;
                case 1:
                    h.Media.SetMedia(CoordinatorHarness.Track($"Track {i}"));
                    break;
                default:
                    h.Post(new IslandEvent.ExpandRequested());
                    break;
            }

            h.Advance(0.05);
        }

        // Make sure a live timer exists, so the zero count after Dispose is meaningful.
        h.Volume.SetLevel(5);
        Assert.Equal(1, h.Scheduler.PendingCount);

        h.Coordinator.Dispose();
        Assert.Equal(0, h.Scheduler.PendingCount);

        var emitted = h.Emitted.Count;
        var fired = h.Scheduler.FiredCount;
        var state = h.State;

        // Posts and service events after Dispose must not throw, change state, or arm timers.
        h.Post(new IslandEvent.ExpandRequested());
        h.Post(new IslandEvent.VolumeChanged(new VolumeInfo(9, false)));
        h.Post(new IslandEvent.MediaChanged(CoordinatorHarness.Track("After dispose"), TrackChanged: true));
        h.Volume.SetLevel(7);
        h.Media.SetMedia(CoordinatorHarness.Track("After dispose 2"));
        h.Advance(60);

        Assert.Equal(0, h.Scheduler.PendingCount);
        Assert.Equal(fired, h.Scheduler.FiredCount);
        Assert.Equal(emitted, h.Emitted.Count);
        Assert.Equal(state, h.State);
    }

    [Fact]
    public void Stale_callbacks_do_not_change_state()
    {
        var scheduler = new RecordingScheduler();
        using var h = new CoordinatorHarness(scheduler);
        h.Start();

        h.Volume.SetLevel(10);                                     // timer A
        h.Volume.SetLevel(20);                                     // timer B, A disposed
        h.Post(new IslandEvent.ExpandRequested());                 // timer C, B disposed
        Assert.Equal(3, scheduler.All.Count);
        var a = scheduler.All[0];
        var b = scheduler.All[1];
        var c = scheduler.All[2];
        Assert.True(a.Disposed);
        Assert.True(b.Disposed);
        Assert.False(c.Disposed);

        // Callbacks from superseded arms, dispatched after their handles were disposed, must not touch the Expanded state.
        var emitted = h.Emitted.Count;
        a.Callback();
        b.Callback();
        Assert.Equal(IslandMode.Expanded, h.Mode);
        Assert.Equal(emitted, h.Emitted.Count);

        c.Callback();
        Assert.Equal(IslandMode.Compact, h.Mode);
        emitted = h.Emitted.Count;

        // Late callbacks from every earlier arm must not regress the settled state or emit anything.
        a.Callback();
        b.Callback();
        c.Callback();
        Assert.Equal(IslandMode.Compact, h.Mode);
        Assert.Equal(emitted, h.Emitted.Count);
        Assert.Equal(20, h.State.Volume!.Level);
    }
}
