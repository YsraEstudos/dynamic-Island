using Island.Core.Tools.Timers;

namespace Island.Core.Tests.Tools.Timers;

public sealed class TimerFormatTests
{
    [Theory]
    [InlineData(300_000, "05:00")]
    [InlineData(0, "00:00")]
    [InlineData(-3_000, "00:00")]
    [InlineData(1, "00:01")]
    [InlineData(9_001, "00:10")]
    [InlineData(59_100, "01:00")]
    [InlineData(3_600_000, "1:00:00")]
    [InlineData(3_723_000, "1:02:03")]
    public void Countdown_rounds_up_and_switches_to_hours_from_one_hour(long milliseconds, string expected)
    {
        Assert.Equal(expected, TimerFormat.Countdown(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Theory]
    [InlineData(0, "00:00,00")]
    [InlineData(-1, "00:00,00")]
    [InlineData(1_234, "00:01,23")]
    [InlineData(59_999, "00:59,99")]
    [InlineData(3_599_995, "59:59,99")]
    [InlineData(3_600_000, "1:00:00,00")]
    public void Stopwatch_truncates_hundredths_and_switches_to_hours_from_one_hour(long milliseconds, string expected)
    {
        Assert.Equal(expected, TimerFormat.Stopwatch(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Theory]
    [InlineData(10, "10 s")]
    [InlineData(60, "1 min")]
    [InlineData(70, "1 min 10 s")]
    [InlineData(1_500, "25 min")]
    [InlineData(10_800, "180 min")]
    public void Duration_label_uses_minutes_and_seconds(int seconds, string expected)
    {
        Assert.Equal(expected, TimerFormat.Duration(TimeSpan.FromSeconds(seconds)));
    }
}
