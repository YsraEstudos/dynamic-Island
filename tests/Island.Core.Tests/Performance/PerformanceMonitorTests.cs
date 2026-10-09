using Island.Core.Abstractions;
using Island.Core.Fakes;
using Island.Core.Models;
using Island.Core.Performance;

namespace Island.Core.Tests.Performance;

public class PerformanceMonitorTests
{
    private readonly ManualScheduler _scheduler = new();
    private readonly ScriptedSampler _sampler = new();
    private readonly InMemorySettingsStore _store = new(PerformanceAlertSettings.Default);

    private PerformanceMonitor CreateMonitor() =>
        new(_sampler, _store, _scheduler, clock: () => _scheduler.Now);

    [Fact]
    public void Nothing_is_sampled_while_the_widget_is_hidden_and_alerts_are_off()
    {
        _store.Stored = PerformanceAlertSettings.Default with { Enabled = false };
        using PerformanceMonitor monitor = CreateMonitor();

        monitor.Start();
        _scheduler.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(0, _scheduler.PendingCount);
        Assert.Equal(0, _sampler.Calls);
    }

    [Fact]
    public void A_visible_widget_samples_every_second_starting_at_once()
    {
        using PerformanceMonitor monitor = CreateMonitor();
        monitor.Start();

        monitor.SetWidgetVisible(true);
        _scheduler.Advance(TimeSpan.Zero);
        Assert.Equal(1, _sampler.Calls);

        _scheduler.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(6, _sampler.Calls);
    }

    [Fact]
    public void With_alerts_on_and_the_widget_hidden_it_samples_every_three_seconds()
    {
        using PerformanceMonitor monitor = CreateMonitor();
        monitor.Start();

        _scheduler.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(1, _sampler.Calls);

        _scheduler.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(3, _sampler.Calls);
    }

    [Fact]
    public void Hiding_the_widget_falls_back_to_the_alert_cadence()
    {
        using PerformanceMonitor monitor = CreateMonitor();
        monitor.Start();
        monitor.SetWidgetVisible(true);
        _scheduler.Advance(TimeSpan.Zero);
        Assert.Equal(1, _sampler.Calls);

        monitor.SetWidgetVisible(false);
        _scheduler.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(2, _sampler.Calls);
    }

    [Fact]
    public void Turning_alerts_off_with_the_widget_hidden_stops_sampling()
    {
        using PerformanceMonitor monitor = CreateMonitor();
        monitor.Start();
        Assert.Equal(1, _scheduler.PendingCount);

        monitor.SetAlerts(PerformanceAlertSettings.Default with { Enabled = false });
        _scheduler.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(0, _scheduler.PendingCount);
        Assert.Equal(0, _sampler.Calls);
    }

    [Fact]
    public void Latest_and_history_follow_the_samples()
    {
        _sampler.Next = PerformanceSnapshot.Empty with { CpuPercent = 42, GpuPercent = 7 };
        using PerformanceMonitor monitor = CreateMonitor();
        monitor.Start();

        _scheduler.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal<double?>(42.0, monitor.Latest?.CpuPercent);
        Assert.Equal(new double?[] { 42 }, monitor.CpuHistory());
        Assert.Equal(new double?[] { 7 }, monitor.GpuHistory());
    }

    [Fact]
    public void A_hot_sensor_raises_one_alert_and_stays_quiet_while_hot()
    {
        _sampler.Next = PerformanceSnapshot.Empty with { CpuTempC = 90 };
        using PerformanceMonitor monitor = CreateMonitor();
        var alerts = new List<TemperatureAlert>();
        monitor.AlertRaised += alerts.Add;
        monitor.Start();

        _scheduler.Advance(TimeSpan.FromSeconds(3));
        _scheduler.Advance(TimeSpan.FromSeconds(3));

        var alert = Assert.Single(alerts);
        Assert.Equal(AlertSensor.Cpu, alert.Sensor);
        Assert.Equal(85, alert.LimitC);
    }

    [Fact]
    public void A_failing_sample_is_skipped_and_the_timer_keeps_running()
    {
        _sampler.ThrowNext = true;
        using PerformanceMonitor monitor = CreateMonitor();
        monitor.Start();

        _scheduler.Advance(TimeSpan.FromSeconds(3));
        Assert.Null(monitor.Latest);

        _scheduler.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(2, _sampler.Calls);
        Assert.NotNull(monitor.Latest);
    }

    [Fact]
    public void A_throwing_subscriber_does_not_stop_sampling()
    {
        using PerformanceMonitor monitor = CreateMonitor();
        monitor.SnapshotChanged += _ => throw new InvalidOperationException("subscriber bug");
        monitor.Start();

        _scheduler.Advance(TimeSpan.FromSeconds(3));
        _scheduler.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(2, _sampler.Calls);
    }

    [Fact]
    public void SetAlerts_clamps_saves_and_applies_the_limits()
    {
        using PerformanceMonitor monitor = CreateMonitor();

        monitor.SetAlerts(new PerformanceAlertSettings(true, 200, 10));

        Assert.Equal(new PerformanceAlertSettings(true, 105, 60), monitor.Alerts);
        Assert.Equal(monitor.Alerts, _store.Stored);
    }

    [Fact]
    public void Dispose_cancels_the_pending_timer()
    {
        var monitor = CreateMonitor();
        monitor.Start();
        Assert.Equal(1, _scheduler.PendingCount);

        monitor.Dispose();

        Assert.Equal(0, _scheduler.PendingCount);
    }

    private sealed class ScriptedSampler : IPerformanceSampler
    {
        public int Calls { get; private set; }
        public PerformanceSnapshot Next { get; set; } = PerformanceSnapshot.Empty;
        public bool ThrowNext { get; set; }

        public PerformanceSnapshot Sample()
        {
            Calls++;
            if (ThrowNext)
            {
                ThrowNext = false;
                throw new InvalidOperationException("sensor read failed");
            }
            return Next;
        }

        public void Dispose()
        {
        }
    }

    private sealed class InMemorySettingsStore(PerformanceAlertSettings initial) : IPerformanceSettingsStore
    {
        public PerformanceAlertSettings Stored { get; set; } = initial;

        public PerformanceAlertSettings Load() => Stored;

        public void Save(PerformanceAlertSettings settings) => Stored = settings;
    }
}
