using Island.Core.Abstractions;
using Island.Core.Fakes;
using Island.Core.Idle;

namespace Island.Core.Tests.Idle;

public class WeatherRefresherTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 10, 14, 0, 0, TimeSpan.FromHours(-3));

    private readonly ManualScheduler _scheduler = new();
    private readonly InMemoryWeatherCache _cache = new();
    private readonly StubWeatherService _service = new();
    private DateTimeOffset _now = Start;

    /// <summary>Scripted service: counts calls, and can be told to fail or throw.</summary>
    private sealed class StubWeatherService : IWeatherService
    {
        public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.MinValue;
        public bool Fail { get; set; }
        public bool Throw { get; set; }
        public int ResolveCalls { get; private set; }
        public int FetchCalls { get; private set; }

        public Task<WeatherPlace?> ResolvePlaceAsync(string city, CancellationToken cancellationToken)
        {
            ResolveCalls++;
            if (Throw) throw new InvalidOperationException("boom");
            return Task.FromResult<WeatherPlace?>(Fail ? null : new WeatherPlace(city, -23.5, -46.6));
        }

        public Task<WeatherSnapshot?> FetchAsync(WeatherPlace place, CancellationToken cancellationToken)
        {
            FetchCalls++;
            if (Throw) throw new InvalidOperationException("boom");
            return Task.FromResult<WeatherSnapshot?>(Fail ? null : new WeatherSnapshot(21.5, 3, 30, Clock()));
        }
    }

    private WeatherRefresher Create()
    {
        _service.Clock = () => _now;
        return new WeatherRefresher(_service, _cache, _scheduler, () => _now);
    }

    /// <summary>Moves the clock and the scheduler together, one minute at a time, so timers see the right time.</summary>
    private void Run(TimeSpan total)
    {
        if (total == TimeSpan.Zero)
        {
            _scheduler.Advance(TimeSpan.Zero);
            return;
        }

        var step = TimeSpan.FromMinutes(1);
        for (var elapsed = TimeSpan.Zero; elapsed < total; elapsed += step)
        {
            _now += step;
            _scheduler.Advance(step);
        }
    }

    [Fact]
    public void Outside_idle_nothing_is_scheduled_and_nothing_is_read()
    {
        using var refresher = Create();

        Run(TimeSpan.FromHours(2));

        Assert.Equal(0, _scheduler.PendingCount);
        Assert.Equal(0, _service.FetchCalls);
    }

    [Fact]
    public void Entering_idle_with_no_reading_reads_at_once()
    {
        using var refresher = Create();

        refresher.SetIdle(true);
        Run(TimeSpan.Zero);

        Assert.Equal(1, _service.FetchCalls);
        Assert.NotNull(refresher.Current);
    }

    [Fact]
    public void While_idle_it_reads_at_most_once_per_interval()
    {
        using var refresher = Create();

        refresher.SetIdle(true);
        Run(TimeSpan.Zero);
        Run(TimeSpan.FromHours(2));

        // Reads at 0, 30, 60, 90 and 120 minutes.
        Assert.Equal(5, _service.FetchCalls);
    }

    [Fact]
    public void A_fresh_cached_reading_waits_for_its_due_time()
    {
        _cache.Save(new WeatherCacheData(
            new WeatherSnapshot(20, 1, 10, Start.AddMinutes(-10)),
            new WeatherPlace(string.Empty, -23.5, -46.6)));
        using var refresher = Create();

        refresher.SetIdle(true);
        Run(TimeSpan.FromMinutes(19));
        Assert.Equal(0, _service.FetchCalls);

        Run(TimeSpan.FromMinutes(1));
        Assert.Equal(1, _service.FetchCalls);
    }

    [Fact]
    public void A_stale_cached_reading_is_renewed_as_soon_as_idle_starts()
    {
        _cache.Save(new WeatherCacheData(
            new WeatherSnapshot(20, 1, 10, Start.AddHours(-1)),
            new WeatherPlace(string.Empty, -23.5, -46.6)));
        using var refresher = Create();

        refresher.SetIdle(true);
        Run(TimeSpan.Zero);

        Assert.Equal(1, _service.FetchCalls);
    }

    [Fact]
    public void Leaving_idle_cancels_the_timer()
    {
        using var refresher = Create();
        refresher.SetIdle(true);
        Run(TimeSpan.Zero);

        refresher.SetIdle(false);
        Run(TimeSpan.FromHours(2));

        Assert.Equal(0, _scheduler.PendingCount);
        Assert.Equal(1, _service.FetchCalls);
    }

    [Fact]
    public void A_failed_read_retries_after_the_backoff_not_the_full_interval()
    {
        _service.Fail = true;
        using var refresher = Create();
        refresher.SetIdle(true);

        // With no place yet, each attempt starts by resolving the place, so the resolve count is the attempt count.
        Run(TimeSpan.Zero);
        Assert.Equal(1, _service.ResolveCalls);
        Assert.Null(refresher.Current);

        Run(TimeSpan.FromMinutes(4));
        Assert.Equal(1, _service.ResolveCalls);

        Run(TimeSpan.FromMinutes(1));
        Assert.Equal(2, _service.ResolveCalls);
    }

    [Fact]
    public void A_successful_read_after_a_failure_returns_to_the_normal_interval()
    {
        _service.Fail = true;
        using var refresher = Create();
        refresher.SetIdle(true);
        Run(TimeSpan.Zero);

        _service.Fail = false;
        Run(TimeSpan.FromMinutes(5));
        Assert.NotNull(refresher.Current);
        Assert.Equal(1, _service.FetchCalls);

        // The reading at minute 5 starts the normal 30-minute cycle, not the 5-minute backoff.
        Run(TimeSpan.FromMinutes(29));
        Assert.Equal(1, _service.FetchCalls);

        Run(TimeSpan.FromMinutes(1));
        Assert.Equal(2, _service.FetchCalls);
    }

    [Fact]
    public void A_service_that_throws_counts_as_a_failure_and_does_not_escape()
    {
        _service.Throw = true;
        using var refresher = Create();

        refresher.SetIdle(true);
        Run(TimeSpan.Zero);

        Assert.Null(refresher.Current);
        Assert.Equal(1, _service.ResolveCalls);
    }

    [Fact]
    public void The_place_is_resolved_once_and_reused_for_later_reads()
    {
        using var refresher = Create();

        refresher.SetIdle(true);
        Run(TimeSpan.Zero);
        Run(TimeSpan.FromHours(2));

        Assert.Equal(1, _service.ResolveCalls);
        Assert.Equal(5, _service.FetchCalls);
    }

    [Fact]
    public void A_reading_survives_a_restart_through_the_cache()
    {
        using (var first = Create())
        {
            first.SetIdle(true);
            Run(TimeSpan.Zero);
        }

        var second = Create();

        Assert.NotNull(second.Current);
        Assert.Equal(1, _service.FetchCalls);
    }

    [Fact]
    public void Changing_the_city_drops_the_old_reading_and_reads_the_new_city_at_once()
    {
        using var refresher = Create();
        refresher.SetIdle(true);
        Run(TimeSpan.Zero);
        int changes = 0;
        refresher.Changed += () => changes++;

        refresher.SetCity("Rio de Janeiro");

        Assert.Null(refresher.Current);
        Assert.Equal(1, changes);

        Run(TimeSpan.Zero);
        Assert.Equal(2, _service.FetchCalls);
        Assert.Equal("Rio de Janeiro", _cache.Load().Place?.Query);
    }

    [Fact]
    public void Setting_the_same_city_again_changes_nothing()
    {
        using var refresher = Create();
        refresher.SetIdle(true);
        Run(TimeSpan.Zero);
        int changes = 0;
        refresher.Changed += () => changes++;

        refresher.SetCity("");
        refresher.SetCity("   ");

        Assert.Equal(0, changes);
        Assert.NotNull(refresher.Current);
    }

    [Fact]
    public void Dispose_cancels_the_timer_and_stops_reads()
    {
        var refresher = Create();
        refresher.SetIdle(true);
        Run(TimeSpan.Zero);

        refresher.Dispose();
        Run(TimeSpan.FromHours(2));

        Assert.Equal(0, _scheduler.PendingCount);
        Assert.Equal(1, _service.FetchCalls);
    }
}
