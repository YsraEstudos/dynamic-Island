using Island.Core.Idle;

namespace Island.Core.Tests.Idle;

public class WeatherPoliciesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, WeatherCategory.Clear)]
    [InlineData(1, WeatherCategory.Clear)]
    [InlineData(2, WeatherCategory.PartlyCloudy)]
    [InlineData(3, WeatherCategory.Cloudy)]
    [InlineData(45, WeatherCategory.Fog)]
    [InlineData(48, WeatherCategory.Fog)]
    [InlineData(53, WeatherCategory.Drizzle)]
    [InlineData(61, WeatherCategory.Rain)]
    [InlineData(65, WeatherCategory.Rain)]
    [InlineData(73, WeatherCategory.Snow)]
    [InlineData(82, WeatherCategory.Rain)]
    [InlineData(86, WeatherCategory.Snow)]
    [InlineData(95, WeatherCategory.Thunderstorm)]
    [InlineData(99, WeatherCategory.Thunderstorm)]
    [InlineData(7, WeatherCategory.Unknown)]
    [InlineData(-1, WeatherCategory.Unknown)]
    [InlineData(1000, WeatherCategory.Unknown)]
    public void Each_code_maps_to_its_category(int code, WeatherCategory expected)
    {
        Assert.Equal(expected, WeatherCodeMapper.Map(code).Category);
    }

    [Fact]
    public void Descriptions_are_short_portuguese_text()
    {
        Assert.Equal("Céu limpo", WeatherCodeMapper.Map(0).Description);
        Assert.Equal("Tempo indefinido", WeatherCodeMapper.Map(500).Description);
    }

    [Fact]
    public void A_fresh_reading_is_shown_and_not_faded()
    {
        Assert.True(WeatherRefreshPolicy.IsShown(T0, T0 + TimeSpan.FromMinutes(5)));
        Assert.False(WeatherRefreshPolicy.IsFaded(T0, T0 + TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Fading_and_hiding_happen_at_their_thresholds()
    {
        DateTimeOffset fade = T0 + WeatherRefreshPolicy.FadeAfter;
        DateTimeOffset hide = T0 + WeatherRefreshPolicy.HideAfter;

        Assert.False(WeatherRefreshPolicy.IsFaded(T0, fade));
        Assert.True(WeatherRefreshPolicy.IsFaded(T0, fade + TimeSpan.FromSeconds(1)));
        Assert.True(WeatherRefreshPolicy.IsShown(T0, hide));
        Assert.False(WeatherRefreshPolicy.IsShown(T0, hide + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void With_no_reading_and_no_failure_the_attempt_is_due_at_once()
    {
        Assert.Equal(TimeSpan.Zero, WeatherRefreshPolicy.DelayUntilNextAttempt(T0, null, null));
    }

    [Fact]
    public void After_a_reading_the_next_attempt_waits_one_interval()
    {
        TimeSpan delay = WeatherRefreshPolicy.DelayUntilNextAttempt(T0 + TimeSpan.FromMinutes(10), T0, null);

        Assert.Equal(TimeSpan.FromMinutes(20), delay);
    }

    [Fact]
    public void After_a_failure_the_next_attempt_waits_the_backoff()
    {
        TimeSpan delay = WeatherRefreshPolicy.DelayUntilNextAttempt(T0 + TimeSpan.FromMinutes(1), T0, T0);

        Assert.Equal(WeatherRefreshPolicy.FailureBackoff - TimeSpan.FromMinutes(1), delay);
    }

    [Fact]
    public void A_failure_older_than_the_last_reading_does_not_hold_the_backoff()
    {
        // Read at T0+20 min after a failure at T0: the reading is newer, so the normal interval applies.
        TimeSpan delay = WeatherRefreshPolicy.DelayUntilNextAttempt(T0 + TimeSpan.FromMinutes(20), T0 + TimeSpan.FromMinutes(20), T0);

        Assert.Equal(WeatherRefreshPolicy.Interval, delay);
    }

    [Fact]
    public void An_overdue_attempt_is_due_at_once_and_a_future_reading_cannot_park_the_timer()
    {
        Assert.Equal(TimeSpan.Zero, WeatherRefreshPolicy.DelayUntilNextAttempt(T0 + TimeSpan.FromHours(2), T0, null));
        Assert.Equal(WeatherRefreshPolicy.Interval, WeatherRefreshPolicy.DelayUntilNextAttempt(T0, T0 + TimeSpan.FromDays(1), null));
    }
}
