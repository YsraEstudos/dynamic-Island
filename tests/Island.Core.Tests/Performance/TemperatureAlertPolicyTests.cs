using Island.Core.Performance;

namespace Island.Core.Tests.Performance;

public class TemperatureAlertPolicyTests
{
    private static readonly PerformanceAlertSettings On = new(true, 85, 83);

    private static PerformanceSnapshot Temps(double? cpu = null, double? gpu = null) =>
        PerformanceSnapshot.Empty with { CpuTempC = cpu, GpuTempC = gpu };

    private static TimeSpan At(int seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Fires_when_the_reading_reaches_the_limit()
    {
        var alert = Assert.Single(new TemperatureAlertPolicy().Evaluate(Temps(cpu: 85), On, At(0)));

        Assert.Equal(AlertSensor.Cpu, alert.Sensor);
        Assert.Equal(85, alert.LimitC);
        Assert.Equal(85.0, alert.Celsius);
    }

    [Fact]
    public void Does_not_fire_below_the_limit()
    {
        Assert.Empty(new TemperatureAlertPolicy().Evaluate(Temps(cpu: 84.9, gpu: 82.9), On, At(0)));
    }

    [Fact]
    public void Stays_quiet_while_still_hot_after_firing()
    {
        var policy = new TemperatureAlertPolicy();
        Assert.Single(policy.Evaluate(Temps(gpu: 90), On, At(0)));

        Assert.Empty(policy.Evaluate(Temps(gpu: 91), On, At(10)));
        Assert.Empty(policy.Evaluate(Temps(gpu: 88), On, At(20)));
    }

    [Fact]
    public void Rearms_only_after_falling_five_degrees_below_the_limit()
    {
        var policy = new TemperatureAlertPolicy();
        Assert.Single(policy.Evaluate(Temps(gpu: 83), On, At(0)));

        // Limit 83: re-arming needs 78 or less. 79 is not enough.
        Assert.Empty(policy.Evaluate(Temps(gpu: 79), On, At(10)));
        Assert.Empty(policy.Evaluate(Temps(gpu: 83), On, At(120)));

        Assert.Empty(policy.Evaluate(Temps(gpu: 78), On, At(130)));
        Assert.Single(policy.Evaluate(Temps(gpu: 83), On, At(200)));
    }

    [Fact]
    public void A_second_alert_waits_out_the_sixty_second_cooldown()
    {
        var policy = new TemperatureAlertPolicy();
        Assert.Single(policy.Evaluate(Temps(cpu: 90), On, At(0)));

        // Re-armed at 10 s, hot again at 30 s: the cooldown from the first alert still holds it back.
        Assert.Empty(policy.Evaluate(Temps(cpu: 80), On, At(10)));
        Assert.Empty(policy.Evaluate(Temps(cpu: 90), On, At(30)));
        Assert.Empty(policy.Evaluate(Temps(cpu: 90), On, At(59)));

        // Still hot once the cooldown ends: it fires then.
        Assert.Single(policy.Evaluate(Temps(cpu: 90), On, At(60)));
    }

    [Fact]
    public void A_cooled_reading_during_the_cooldown_does_not_fire_later()
    {
        var policy = new TemperatureAlertPolicy();
        Assert.Single(policy.Evaluate(Temps(cpu: 90), On, At(0)));

        Assert.Empty(policy.Evaluate(Temps(cpu: 80), On, At(10)));
        Assert.Empty(policy.Evaluate(Temps(cpu: 90), On, At(20)));
        // It cools below the re-arm point before the cooldown ends: the pending hot reading is gone.
        Assert.Empty(policy.Evaluate(Temps(cpu: 70), On, At(40)));
        Assert.Empty(policy.Evaluate(Temps(cpu: 70), On, At(90)));
    }

    [Fact]
    public void A_missing_reading_keeps_the_state()
    {
        var policy = new TemperatureAlertPolicy();
        Assert.Single(policy.Evaluate(Temps(cpu: 90), On, At(0)));

        // No reading must not re-arm the sensor.
        Assert.Empty(policy.Evaluate(Temps(cpu: null), On, At(10)));
        Assert.Empty(policy.Evaluate(Temps(cpu: 90), On, At(120)));
    }

    [Fact]
    public void Disabled_alerts_raise_nothing_and_forget_the_state()
    {
        var policy = new TemperatureAlertPolicy();
        Assert.Single(policy.Evaluate(Temps(cpu: 90), On, At(0)));

        var off = On with { Enabled = false };
        Assert.Empty(policy.Evaluate(Temps(cpu: 95), off, At(10)));

        // Turned back on: fresh state, so the hot reading alerts at once even within the old cooldown.
        Assert.Single(policy.Evaluate(Temps(cpu: 90), On, At(20)));
    }

    [Fact]
    public void Each_sensor_uses_its_own_limit_and_state()
    {
        var policy = new TemperatureAlertPolicy();
        var both = policy.Evaluate(Temps(cpu: 90, gpu: 84), On, At(0));

        Assert.Equal(2, both.Count);
        Assert.Contains(both, a => a.Sensor == AlertSensor.Cpu && a.LimitC == 85);
        Assert.Contains(both, a => a.Sensor == AlertSensor.Gpu && a.LimitC == 83);
    }

    [Fact]
    public void Uses_the_limits_from_the_settings()
    {
        var alerts = new TemperatureAlertPolicy().Evaluate(Temps(cpu: 85, gpu: 81), new(true, 90, 80), At(0));

        var alert = Assert.Single(alerts);
        Assert.Equal(AlertSensor.Gpu, alert.Sensor);
        Assert.Equal(80, alert.LimitC);
    }
}
