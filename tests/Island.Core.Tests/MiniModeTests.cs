using Island.Core.Application;
using Island.Core.Configuration;
using Island.Core.Models;

namespace Island.Core.Tests;

public class MiniModeTests
{
    private static readonly IslandSettings Settings = new();
    private static readonly IslandState MiniState = IslandState.Initial with { Mode = IslandMode.Mini };

    private static MediaInfo Playing() =>
        new("A", "Artist", null, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), "Spotify");

    [Fact]
    public void Minimize_goes_to_mini_without_a_timer()
    {
        var result = IslandStateReducer.Reduce(IslandState.Initial, ReducerFlags.None, new IslandEvent.MinimizeRequested(), Settings);

        Assert.Equal(IslandMode.Mini, result.State.Mode);
        Assert.Equal(TimerAction.Cancel, result.Timer);
    }

    [Fact]
    public void Minimize_from_expanded_cancels_the_idle_timer()
    {
        var expanded = IslandStateReducer.Reduce(IslandState.Initial, ReducerFlags.None, new IslandEvent.ExpandRequested(), Settings);
        var result = IslandStateReducer.Reduce(expanded.State, expanded.Flags, new IslandEvent.MinimizeRequested(), Settings);

        Assert.Equal(IslandMode.Mini, result.State.Mode);
        Assert.Equal(TimerAction.Cancel, result.Timer);
    }

    [Fact]
    public void Passive_events_do_not_leave_mini()
    {
        var volume = IslandStateReducer.Reduce(MiniState, ReducerFlags.None,
            new IslandEvent.VolumeChanged(new VolumeInfo(40, false)), Settings);
        var media = IslandStateReducer.Reduce(MiniState, ReducerFlags.None,
            new IslandEvent.MediaChanged(Playing(), TrackChanged: true), Settings);
        var notice = IslandStateReducer.Reduce(MiniState, ReducerFlags.None,
            new IslandEvent.NoticeRaised(new Notice("T", "S", "timer")), Settings);

        Assert.Equal(IslandMode.Mini, volume.State.Mode);
        Assert.Equal(IslandMode.Mini, media.State.Mode);
        Assert.Equal(IslandMode.Mini, notice.State.Mode);
        Assert.Null(notice.State.Notice);
        Assert.Equal(40, volume.State.Volume?.Level);   // data still updates
    }

    [Fact]
    public void Collapse_restores_compact()
    {
        var result = IslandStateReducer.Reduce(MiniState, ReducerFlags.None, new IslandEvent.CollapseRequested(), Settings);

        Assert.Equal(IslandMode.Compact, result.State.Mode);
    }

    [Theory]
    [InlineData(IslandMode.Expanded)]
    [InlineData(IslandMode.Clipboard)]
    public void Explicit_requests_wake_mini(IslandMode target)
    {
        IslandEvent e = target == IslandMode.Expanded ? new IslandEvent.ExpandRequested() : new IslandEvent.ClipboardRequested();
        var result = IslandStateReducer.Reduce(MiniState, ReducerFlags.None, e, Settings);

        Assert.Equal(target, result.State.Mode);
    }

    [Fact]
    public void Expiry_and_suspend_keep_mini()
    {
        var expired = IslandStateReducer.Reduce(MiniState, ReducerFlags.None, new IslandEvent.TemporaryStateExpired(), Settings);
        var suspended = IslandStateReducer.Reduce(MiniState, ReducerFlags.None, new IslandEvent.PausedChanged(true), Settings);

        Assert.Equal(IslandMode.Mini, expired.State.Mode);
        Assert.Equal(IslandMode.Mini, suspended.State.Mode);
        Assert.True(suspended.State.Suspended);
    }
}

public class MiniPersistenceTests
{
    [Fact]
    public void Start_with_saved_minimized_flag_begins_as_mini()
    {
        using var h = new CoordinatorHarness { Settings = new IslandSettings { Minimized = true } };

        h.Coordinator.Start();

        Assert.Equal(IslandMode.Mini, h.Mode);
    }

    [Fact]
    public void Start_without_the_flag_begins_as_compact()
    {
        using var h = new CoordinatorHarness();

        h.Coordinator.Start();

        Assert.Equal(IslandMode.Compact, h.Mode);
    }

    [Fact]
    public void Collapse_after_a_minimized_start_restores_compact()
    {
        using var h = new CoordinatorHarness { Settings = new IslandSettings { Minimized = true } };
        h.Coordinator.Start();

        h.Post(new IslandEvent.CollapseRequested());

        Assert.Equal(IslandMode.Compact, h.Mode);
    }
}
