using Island.App.ViewModels;
using Island.App.Views;
using Island.Core.Application;
using Island.Core.Configuration;
using Island.Core.Fakes;
using Island.Core.Idle;
using Island.Core.Models;
using Island.Core.Pomodoro;

namespace Island.Windows.Tests.Idle;

public class IdlePresenterTests
{
    /// <summary>The whole idle graph on fakes. Built on the WPF test thread, like the presenter in the app.</summary>
    private sealed class Rig : IDisposable
    {
        public ManualScheduler Scheduler { get; } = new();
        public FakeMediaService Media { get; } = new();
        public IslandSettings Settings { get; set; } = new();
        public InMemoryWeatherCache Cache { get; } = new();
        public FakeWeatherService Weather { get; } = new();
        public PomodoroTimer Timer { get; }
        public PomodoroSchedule Schedule { get; }
        public WeatherRefresher Refresher { get; }
        public IslandCoordinator Coordinator { get; }
        public IdlePresenter Presenter { get; }

        public Rig(WeatherCacheData? seed = null)
        {
            if (seed is not null) Cache.Save(seed);
            Timer = new PomodoroTimer(Scheduler, () => Settings);
            Schedule = new PomodoroSchedule(Timer, new AngryPomodoro(Timer, () => "frase de teste"), Scheduler);
            Refresher = new WeatherRefresher(Weather, Cache, Scheduler, () => DateTimeOffset.Now);
            Coordinator = new IslandCoordinator(Media, new FakeVolumeService(), new FakeDisplayService(), Scheduler, () => Settings);
            Coordinator.Start();
            Presenter = new IdlePresenter(Coordinator, Timer, Schedule, Refresher, () => Settings);
        }

        public void Dispose()
        {
            Presenter.Dispose();
            Refresher.Dispose();
            Coordinator.Dispose();
        }
    }

    private static WeatherCacheData Reading(DateTimeOffset fetchedAt, int rain = 40, int code = 3) =>
        new(new WeatherSnapshot(23.4, code, rain, fetchedAt), new WeatherPlace(string.Empty, -8.05, -34.9));

    [Fact]
    public void A_fresh_reading_shows_the_temperature_and_the_rain_chance_and_reads_nothing_now()
    {
        WpfStaTestHost.Run(_ =>
        {
            using var rig = new Rig(Reading(DateTimeOffset.Now.AddMinutes(-5)));

            rig.Presenter.Refresh();

            Assert.True(rig.Presenter.HasContent);
            Assert.True(rig.Presenter.IsWeather);
            Assert.Equal("23°C", rig.Presenter.PrimaryText);
            Assert.Equal("40%", rig.Presenter.RainText);
            Assert.Same(IdleIcons.For(WeatherCategory.Cloudy), rig.Presenter.Glyph);
            Assert.Equal(1.0, rig.Presenter.ContentOpacity);
            Assert.Equal("Nublado, 40% de chance de chuva", rig.Presenter.Description);
            // The reading is fresh, so the first renewal is due in about 25 minutes, not now.
            Assert.Equal(0, rig.Weather.FetchCalls);
        });
    }

    [Fact]
    public void A_scheduled_start_replaces_the_weather_with_its_time()
    {
        WpfStaTestHost.Run(_ =>
        {
            using var rig = new Rig(Reading(DateTimeOffset.Now.AddMinutes(-5)));
            DateTimeOffset at = DateTimeOffset.Now.AddHours(2);
            rig.Schedule.Set(at, ScheduledStartMode.Focus, 1);

            rig.Presenter.Refresh();

            Assert.True(rig.Presenter.HasContent);
            Assert.False(rig.Presenter.IsWeather);
            Assert.Same(IdleIcons.Timer, rig.Presenter.Glyph);
            Assert.Equal(at.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), rig.Presenter.PrimaryText);
            Assert.StartsWith("Pomodoro começa às", rig.Presenter.Description);
        });
    }

    [Fact]
    public void A_scheduled_angry_start_says_so()
    {
        WpfStaTestHost.Run(_ =>
        {
            using var rig = new Rig();
            rig.Schedule.Set(DateTimeOffset.Now.AddHours(2), ScheduledStartMode.Angry, 1);

            rig.Presenter.Refresh();

            Assert.StartsWith("Pomodoro raivoso começa às", rig.Presenter.Description);
        });
    }

    [Fact]
    public void A_running_pomodoro_clears_the_idle_content_and_stops_the_weather_cycle()
    {
        WpfStaTestHost.Run(_ =>
        {
            using var rig = new Rig(Reading(DateTimeOffset.Now.AddMinutes(-5)));
            rig.Presenter.Refresh();
            Assert.True(rig.Presenter.HasContent);

            rig.Timer.StartPlan(1);
            rig.Presenter.Refresh();

            Assert.False(rig.Presenter.HasContent);
            rig.Scheduler.Advance(TimeSpan.FromHours(1));
            Assert.Equal(0, rig.Weather.FetchCalls);
        });
    }

    [Fact]
    public void Playing_media_clears_the_idle_content()
    {
        WpfStaTestHost.Run(_ =>
        {
            using var rig = new Rig(Reading(DateTimeOffset.Now.AddMinutes(-5)));
            rig.Presenter.Refresh();
            Assert.True(rig.Presenter.HasContent);

            rig.Media.SetMedia(new MediaInfo("Faixa", "Artista", null, IsPlaying: true, TimeSpan.Zero, TimeSpan.FromMinutes(3)));
            rig.Presenter.Refresh();

            Assert.False(rig.Presenter.HasContent);
        });
    }

    [Fact]
    public void Leaving_the_compact_capsule_clears_the_idle_content()
    {
        WpfStaTestHost.Run(_ =>
        {
            using var rig = new Rig(Reading(DateTimeOffset.Now.AddMinutes(-5)));
            rig.Presenter.Refresh();

            rig.Coordinator.Post(new IslandEvent.ExpandRequested());
            rig.Presenter.Refresh();

            Assert.False(rig.Presenter.HasContent);
            rig.Scheduler.Advance(TimeSpan.FromHours(1));
            Assert.Equal(0, rig.Weather.FetchCalls);
        });
    }

    [Fact]
    public void Turning_the_setting_off_clears_the_content_and_stops_the_weather_cycle()
    {
        WpfStaTestHost.Run(_ =>
        {
            using var rig = new Rig(Reading(DateTimeOffset.Now.AddMinutes(-5)));
            rig.Presenter.Refresh();

            rig.Settings = rig.Settings with { IdleModeEnabled = false };
            rig.Presenter.Refresh();

            Assert.False(rig.Presenter.HasContent);
            rig.Scheduler.Advance(TimeSpan.FromHours(1));
            Assert.Equal(0, rig.Weather.FetchCalls);
        });
    }

    [Fact]
    public void An_old_reading_is_faded_and_a_very_old_one_is_not_shown()
    {
        WpfStaTestHost.Run(_ =>
        {
            using var faded = new Rig(Reading(DateTimeOffset.Now.AddMinutes(-60)));
            faded.Presenter.Refresh();
            Assert.True(faded.Presenter.HasContent);
            Assert.Equal(0.45, faded.Presenter.ContentOpacity, precision: 6);
            Assert.EndsWith("(última leitura)", faded.Presenter.Description);
        });

        WpfStaTestHost.Run(_ =>
        {
            using var gone = new Rig(Reading(DateTimeOffset.Now.AddHours(-4)));
            gone.Presenter.Refresh();
            Assert.False(gone.Presenter.HasContent);
        });
    }

    [Fact]
    public void A_city_change_reaches_the_weather_refresher()
    {
        WpfStaTestHost.Run(_ =>
        {
            using var rig = new Rig(Reading(DateTimeOffset.Now.AddMinutes(-5)));
            rig.Presenter.Refresh();

            rig.Settings = rig.Settings with { WeatherCity = "Natal" };
            rig.Presenter.Refresh();

            // The old reading belonged to another place, so it is gone until the new city is read.
            Assert.Null(rig.Refresher.Current);
            Assert.False(rig.Presenter.HasContent);
        });
    }
}
