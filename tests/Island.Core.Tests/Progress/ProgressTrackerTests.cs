using Island.Core.Fakes;
using Island.Core.Models;
using Island.Core.Progress;

namespace Island.Core.Tests.Progress;

public class ProgressTrackerTests : IDisposable
{
    private readonly FakeProgressFeed _feed = new();
    private readonly ManualScheduler _scheduler = new();
    private readonly List<Notice> _notices = new();
    private readonly ProgressTracker _tracker;
    private DateTimeOffset _now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private int _changes;

    public ProgressTrackerTests()
    {
        _tracker = new ProgressTracker(_feed, _scheduler, () => _now);
        _tracker.NoticeRaised += _notices.Add;
        _tracker.Changed += () => _changes++;
    }

    public void Dispose() => _tracker.Dispose();

    private static ProgressUpdate Update(string id, ProgressState state, double? percent = null, string? message = null, string title = "Cortar: aula.mp4") =>
        new(id, title, "Corte", state, percent, message, "SuperPrograma");

    [Fact]
    public void Running_update_is_listed_and_raises_changed()
    {
        _feed.Push(Update("a", ProgressState.Running, 42));

        ProgressTaskInfo task = Assert.Single(_tracker.Snapshot());
        Assert.Equal("a", task.Id);
        Assert.Equal("Cortar: aula.mp4", task.Title);
        Assert.Equal(ProgressState.Running, task.State);
        Assert.Equal(42.0, task.Percent);
        Assert.True(task.IsActive);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Identical_update_does_not_raise_changed()
    {
        _feed.Push(Update("a", ProgressState.Running, 42));
        _feed.Push(Update("a", ProgressState.Running, 42));

        Assert.Equal(1, _changes);
    }

    [Fact]
    public void Changed_fires_for_each_observable_change()
    {
        _feed.Push(Update("a", ProgressState.Running, 10));
        _feed.Push(Update("a", ProgressState.Running, 20));

        Assert.Equal(2, _changes);
        Assert.Equal(20.0, Assert.Single(_tracker.Snapshot()).Percent);
    }

    [Fact]
    public void Done_raises_one_notice_and_lingers_four_seconds()
    {
        _feed.Push(Update("a", ProgressState.Running, 50));
        _feed.Push(Update("a", ProgressState.Done, 100));

        Notice notice = Assert.Single(_notices);
        Assert.Equal("Concluido", notice.Title);
        Assert.Equal("Cortar: aula.mp4", notice.Subtitle);
        Assert.Equal("check", notice.Glyph);
        Assert.Equal(ProgressState.Done, Assert.Single(_tracker.Snapshot()).State);

        _scheduler.Advance(TimeSpan.FromMilliseconds(3900));
        Assert.Single(_tracker.Snapshot());

        _scheduler.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Empty(_tracker.Snapshot());
    }

    [Fact]
    public void Error_notice_carries_the_message()
    {
        _feed.Push(Update("a", ProgressState.Running, 30));
        _feed.Push(Update("a", ProgressState.Error, null, "ffmpeg falhou"));

        Notice notice = Assert.Single(_notices);
        Assert.Equal("Falhou", notice.Title);
        Assert.Equal("Cortar: aula.mp4: ffmpeg falhou", notice.Subtitle);
        Assert.Equal("task-error", notice.Glyph);
    }

    [Fact]
    public void Error_without_message_uses_the_title_alone()
    {
        _feed.Push(Update("a", ProgressState.Error));

        Assert.Equal("Cortar: aula.mp4", Assert.Single(_notices).Subtitle);
    }

    [Fact]
    public void Cancelled_is_removed_at_once_without_notice_or_timer()
    {
        _feed.Push(Update("a", ProgressState.Running, 30));
        _feed.Push(Update("a", ProgressState.Cancelled));

        Assert.Empty(_tracker.Snapshot());
        Assert.Empty(_notices);
        Assert.Equal(0, _scheduler.PendingCount);
    }

    [Fact]
    public void Finished_task_notifies_only_once_and_late_updates_are_ignored()
    {
        _feed.Push(Update("a", ProgressState.Running, 50));
        _feed.Push(Update("a", ProgressState.Done, 100));
        _feed.Push(Update("a", ProgressState.Done, 100));
        _feed.Push(Update("a", ProgressState.Running, 60));

        Assert.Single(_notices);
        Assert.Equal(ProgressState.Done, Assert.Single(_tracker.Snapshot()).State);
    }

    [Fact]
    public void Late_update_after_removal_does_not_bring_the_task_back()
    {
        _feed.Push(Update("a", ProgressState.Done, 100));
        _scheduler.Advance(ProgressTracker.FinishedLinger);

        _feed.Push(Update("a", ProgressState.Running, 60));

        Assert.Empty(_tracker.Snapshot());
    }

    [Fact]
    public void Cancelled_for_an_unknown_task_is_ignored()
    {
        _feed.Push(Update("a", ProgressState.Cancelled));

        Assert.Empty(_tracker.Snapshot());
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void Done_for_an_unknown_task_is_listed_and_notifies()
    {
        _feed.Push(Update("a", ProgressState.Done, 100));

        Assert.Single(_notices);
        Assert.Single(_tracker.Snapshot());
    }

    [Fact]
    public void Eta_is_elapsed_times_remaining_over_done()
    {
        _feed.Push(Update("a", ProgressState.Running, 50));
        _now += TimeSpan.FromSeconds(10);

        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(_tracker.Snapshot()).Eta);
    }

    [Fact]
    public void Eta_is_rounded_to_whole_seconds()
    {
        _feed.Push(Update("a", ProgressState.Running, 30));
        _now += TimeSpan.FromSeconds(10);

        // 10 * 70 / 30 = 23.33 s
        Assert.Equal(TimeSpan.FromSeconds(23), Assert.Single(_tracker.Snapshot()).Eta);
    }

    [Theory]
    [InlineData(ProgressState.Running, 2.0)]
    [InlineData(ProgressState.Running, 100.0)]
    [InlineData(ProgressState.Running, null)]
    [InlineData(ProgressState.Queued, 50.0)]
    public void Eta_is_null_outside_running_between_3_and_99_percent(ProgressState state, double? percent)
    {
        _feed.Push(Update("a", state, percent));
        _now += TimeSpan.FromSeconds(30);

        Assert.Null(Assert.Single(_tracker.Snapshot()).Eta);
    }

    [Fact]
    public void Eta_is_never_negative_when_the_clock_goes_back()
    {
        _feed.Push(Update("a", ProgressState.Running, 50));
        _now -= TimeSpan.FromSeconds(5);

        Assert.Equal(TimeSpan.Zero, Assert.Single(_tracker.Snapshot()).Eta);
    }

    [Fact]
    public void Keeps_at_most_sixteen_tasks_and_drops_the_oldest()
    {
        for (int i = 0; i < 17; i++)
        {
            _feed.Push(Update($"t{i}", ProgressState.Running, 10));
            _now += TimeSpan.FromSeconds(1);
        }

        var tasks = _tracker.Snapshot();
        Assert.Equal(16, tasks.Count);
        Assert.DoesNotContain(tasks, t => t.Id == "t0");
        Assert.Equal("t1", tasks[0].Id);
    }

    [Fact]
    public void Dropped_removes_active_tasks_without_notice()
    {
        _feed.Push(Update("a", ProgressState.Running, 10));
        _feed.Push(Update("b", ProgressState.Running, 20));

        _feed.Drop("a");

        Assert.Equal("b", Assert.Single(_tracker.Snapshot()).Id);
        Assert.Empty(_notices);
    }

    [Fact]
    public void Dropped_keeps_a_finished_task_until_its_linger_ends()
    {
        _feed.Push(Update("a", ProgressState.Done, 100));

        _feed.Drop("a");

        Assert.Single(_tracker.Snapshot());
    }

    [Fact]
    public void Dropped_task_can_report_again_later()
    {
        _feed.Push(Update("a", ProgressState.Running, 10));
        _feed.Drop("a");

        _feed.Push(Update("a", ProgressState.Running, 15));

        Assert.Equal(15.0, Assert.Single(_tracker.Snapshot()).Percent);
    }

    [Fact]
    public void Snapshot_lists_active_tasks_first_then_finished()
    {
        _feed.Push(Update("done", ProgressState.Done, 100));
        _now += TimeSpan.FromSeconds(1);
        _feed.Push(Update("running", ProgressState.Running, 10));

        var tasks = _tracker.Snapshot();
        Assert.Equal(new[] { "running", "done" }, tasks.Select(t => t.Id));
    }

    [Fact]
    public void Summary_averages_the_known_percents_of_active_tasks_only()
    {
        _feed.Push(Update("a", ProgressState.Running, 20));
        _feed.Push(Update("b", ProgressState.Running, 40));
        _feed.Push(Update("c", ProgressState.Done, 100));

        ProgressSummary summary = _tracker.Summary;

        Assert.Equal(2, summary.ActiveCount);
        Assert.Equal(0.3, summary.Fraction, 6);
        Assert.False(summary.IsIndeterminate);
    }

    [Fact]
    public void Summary_counts_queued_tasks_without_percent_but_leaves_them_out_of_the_average()
    {
        _feed.Push(Update("a", ProgressState.Running, 20));
        _feed.Push(Update("b", ProgressState.Queued, null));

        ProgressSummary summary = _tracker.Summary;

        Assert.Equal(2, summary.ActiveCount);
        Assert.Equal(0.2, summary.Fraction, 6);
        Assert.False(summary.IsIndeterminate);
    }

    [Fact]
    public void Summary_is_indeterminate_when_no_active_task_reports_a_percent()
    {
        _feed.Push(Update("a", ProgressState.Queued, null));
        _feed.Push(Update("b", ProgressState.Running, null));

        ProgressSummary summary = _tracker.Summary;

        Assert.Equal(2, summary.ActiveCount);
        Assert.Equal(0.0, summary.Fraction);
        Assert.True(summary.IsIndeterminate);
    }

    [Fact]
    public void Summary_is_none_when_nothing_is_active()
    {
        _feed.Push(Update("a", ProgressState.Done, 100));

        Assert.Equal(ProgressSummary.None, _tracker.Summary);
    }

    [Fact]
    public void Expiry_raises_changed()
    {
        _feed.Push(Update("a", ProgressState.Done, 100));
        int before = _changes;

        _scheduler.Advance(ProgressTracker.FinishedLinger);

        Assert.Equal(before + 1, _changes);
    }

    [Fact]
    public void Start_starts_the_feed_once()
    {
        var feed = new SpyFeed();
        using var tracker = new ProgressTracker(feed, _scheduler, () => _now);

        tracker.Start();
        tracker.Start();

        Assert.Equal(1, feed.Starts);
    }

    [Fact]
    public void Dispose_discards_timers_and_stops_listening()
    {
        _feed.Push(Update("a", ProgressState.Done, 100));
        Assert.Equal(1, _scheduler.PendingCount);

        _tracker.Dispose();
        _feed.Push(Update("b", ProgressState.Running, 10));

        Assert.Equal(0, _scheduler.PendingCount);
        Assert.Empty(_tracker.Snapshot());
    }

    /// <summary>Counts Start calls. It never raises events, so the subscriptions are accepted and ignored.</summary>
    internal sealed class SpyFeed : IProgressFeed
    {
        public int Starts { get; private set; }

        public event Action<ProgressUpdate>? Updated { add { } remove { } }

        public event Action<IReadOnlyList<string>>? Dropped { add { } remove { } }

        public void Start() => Starts++;

        public void Dispose() { }
    }
}
