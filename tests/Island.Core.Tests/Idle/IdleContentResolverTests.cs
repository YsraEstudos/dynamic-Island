using Island.Core.Idle;
using Island.Core.Pomodoro;

namespace Island.Core.Tests.Idle;

public class IdleContentResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 14, 0, 0, TimeSpan.FromHours(-3));
    private static readonly ScheduledStart Scheduled = new(Now.AddMinutes(90), ScheduledStartMode.Focus, 1);

    private static WeatherSnapshot Reading(DateTimeOffset? at = null) =>
        new(23.4, 3, 40, at ?? Now.AddMinutes(-5));

    // Plain idle: Compact, visible, nothing playing, setting on. Tests change one thing at a time.
    private static IdleInputs Idle(
        bool enabled = true, bool compact = true, bool suspended = false, bool pomodoro = false, bool media = false,
        ScheduledStart? scheduled = null, WeatherSnapshot? weather = null) =>
        new(enabled, compact, suspended, pomodoro, media, scheduled, weather, Now);

    [Fact]
    public void Nothing_shows_when_the_setting_is_off()
    {
        var content = IdleContentResolver.Resolve(Idle(enabled: false, weather: Reading(), scheduled: Scheduled));

        Assert.Equal(IdleContent.None, content);
    }

    [Fact]
    public void A_plain_compact_capsule_is_idle()
    {
        Assert.True(IdleContentResolver.IsIdle(Idle()));
    }

    [Theory]
    // Not the Compact capsule: Mini, Volume, Notice, the shelf or the clipboard.
    [InlineData(false, false, false, false)]
    // Suspended: paused or a full-screen app hides the island.
    [InlineData(true, true, false, false)]
    // A pomodoro is in progress: its countdown owns the capsule.
    [InlineData(true, false, true, false)]
    // Media is playing: the equalizer owns the capsule.
    [InlineData(true, false, false, true)]
    public void Anything_else_on_screen_keeps_the_capsule_out_of_idle(bool compact, bool suspended, bool pomodoro, bool media)
    {
        var inputs = Idle(compact: compact, suspended: suspended, pomodoro: pomodoro, media: media, weather: Reading());

        Assert.False(IdleContentResolver.IsIdle(inputs));
        Assert.Equal(IdleContent.None, IdleContentResolver.Resolve(inputs));
    }

    [Fact]
    public void A_pomodoro_in_progress_keeps_the_capsule_for_its_countdown()
    {
        var content = IdleContentResolver.Resolve(Idle(pomodoro: true, scheduled: Scheduled, weather: Reading()));

        Assert.Equal(IdleContent.None, content);
    }

    [Fact]
    public void Playing_media_keeps_the_capsule_for_the_equalizer_over_a_scheduled_start_and_weather()
    {
        var content = IdleContentResolver.Resolve(Idle(media: true, scheduled: Scheduled, weather: Reading()));

        Assert.Equal(IdleContent.None, content);
    }

    [Fact]
    public void A_scheduled_start_beats_the_weather()
    {
        var content = IdleContentResolver.Resolve(Idle(scheduled: Scheduled, weather: Reading()));

        Assert.Equal(new IdleContent.ScheduledPomodoro(Scheduled), content);
    }

    [Fact]
    public void Fresh_weather_shows_when_nothing_else_is_pending()
    {
        var reading = Reading();

        var content = IdleContentResolver.Resolve(Idle(weather: reading));

        Assert.Equal(new IdleContent.Weather(reading, Faded: false), content);
    }

    [Fact]
    public void Weather_with_no_reading_shows_nothing()
    {
        Assert.Equal(IdleContent.None, IdleContentResolver.Resolve(Idle()));
    }

    [Fact]
    public void Weather_older_than_the_fade_age_shows_faded()
    {
        var reading = Reading(at: Now - WeatherRefreshPolicy.FadeAfter - TimeSpan.FromMinutes(1));

        var content = IdleContentResolver.Resolve(Idle(weather: reading));

        Assert.Equal(new IdleContent.Weather(reading, Faded: true), content);
    }

    [Fact]
    public void Weather_older_than_the_hide_age_is_not_shown()
    {
        var reading = Reading(at: Now - WeatherRefreshPolicy.HideAfter - TimeSpan.FromMinutes(1));

        Assert.Equal(IdleContent.None, IdleContentResolver.Resolve(Idle(weather: reading)));
    }

    [Fact]
    public void A_scheduled_start_still_shows_when_the_weather_is_too_old()
    {
        var reading = Reading(at: Now - WeatherRefreshPolicy.HideAfter - TimeSpan.FromHours(1));

        Assert.Equal(new IdleContent.ScheduledPomodoro(Scheduled), IdleContentResolver.Resolve(Idle(scheduled: Scheduled, weather: reading)));
    }

    [Fact]
    public void Leaving_the_pomodoro_or_media_brings_the_idle_content_back()
    {
        var reading = Reading();

        Assert.Equal(IdleContent.None, IdleContentResolver.Resolve(Idle(pomodoro: true, weather: reading)));
        Assert.Equal(new IdleContent.Weather(reading, false), IdleContentResolver.Resolve(Idle(pomodoro: false, weather: reading)));
        Assert.Equal(IdleContent.None, IdleContentResolver.Resolve(Idle(media: true, weather: reading)));
        Assert.Equal(new IdleContent.Weather(reading, false), IdleContentResolver.Resolve(Idle(media: false, weather: reading)));
    }
}
