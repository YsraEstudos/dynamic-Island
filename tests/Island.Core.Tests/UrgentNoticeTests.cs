using Island.Core.Application;
using Island.Core.Configuration;
using Island.Core.Models;

namespace Island.Core.Tests;

public class UrgentNoticeTests
{
    private static readonly IslandSettings Settings = new();
    private static readonly Notice UrgentNotice = new("Hora do descanso", "Pomodoro concluído", "timer", Urgent: true);
    private static readonly Notice NormalNotice = new("Copiado");

    private static ReductionResult Notify(IslandMode current, Notice notice, ReducerFlags? flags = null, IslandSettings? settings = null) =>
        IslandStateReducer.Reduce(IslandState.Initial with { Mode = current }, flags ?? ReducerFlags.None,
            new IslandEvent.NoticeRaised(notice), settings ?? Settings);

    [Theory]
    [InlineData(IslandMode.Compact)]
    [InlineData(IslandMode.Mini)]
    [InlineData(IslandMode.Volume)]
    [InlineData(IslandMode.MediaPreview)]
    [InlineData(IslandMode.Expanded)]
    [InlineData(IslandMode.Clipboard)]
    public void Urgent_notice_replaces_the_current_mode(IslandMode current)
    {
        var result = Notify(current, UrgentNotice);

        Assert.Equal(IslandMode.Notice, result.State.Mode);
        Assert.Equal(UrgentNotice, result.State.Notice);
        Assert.Equal(TimerAction.Arm, result.Timer);
        Assert.Equal(TimeSpan.FromSeconds(6), result.Delay);
    }

    [Fact]
    public void Urgent_notice_is_dropped_in_customize()
    {
        var result = Notify(IslandMode.Customize, UrgentNotice);

        Assert.Equal(IslandMode.Customize, result.State.Mode);
        Assert.Null(result.State.Notice);
    }

    [Theory]
    [InlineData(IslandMode.Expanded)]
    [InlineData(IslandMode.Mini)]
    public void Normal_notice_is_still_dropped_in_protected_or_mini_modes(IslandMode current)
    {
        var result = Notify(current, NormalNotice);

        Assert.Equal(current, result.State.Mode);
        Assert.Null(result.State.Notice);
    }

    [Fact]
    public void Urgent_notice_while_paused_stays_compact()
    {
        var result = Notify(IslandMode.Compact, UrgentNotice, new ReducerFlags(false, true, false));

        Assert.Equal(IslandMode.Compact, result.State.Mode);
        Assert.True(result.State.Suspended);
        Assert.Null(result.State.Notice);
    }

    [Fact]
    public void Urgent_duration_is_at_least_six_seconds()
    {
        var result = Notify(IslandMode.Compact, UrgentNotice, settings: Settings with { NoticeSeconds = 3 });

        Assert.Equal(TimerAction.Arm, result.Timer);
        Assert.Equal(TimeSpan.FromSeconds(6), result.Delay);
    }

    [Fact]
    public void Urgent_duration_keeps_a_longer_notice_setting()
    {
        var result = Notify(IslandMode.Compact, UrgentNotice, settings: Settings with { NoticeSeconds = 10 });

        Assert.Equal(TimerAction.Arm, result.Timer);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Delay);
    }

    [Fact]
    public void Normal_notice_arms_the_notice_setting()
    {
        var result = Notify(IslandMode.Compact, NormalNotice, settings: Settings with { NoticeSeconds = 3 });

        Assert.Equal(TimerAction.Arm, result.Timer);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Delay);
    }
}
