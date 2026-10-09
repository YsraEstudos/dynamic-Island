using Island.Core.Performance;

namespace Island.Core.Tests.Performance;

public class PerformanceSettingsAndNoticesTests
{
    [Fact]
    public void Default_is_on_with_85_and_83_limits()
    {
        Assert.Equal(new PerformanceAlertSettings(true, 85, 83), PerformanceAlertSettings.Default);
    }

    [Fact]
    public void Sanitized_brings_limits_into_range()
    {
        var clean = new PerformanceAlertSettings(true, 20, 200).Sanitized();

        Assert.Equal(new PerformanceAlertSettings(true, 60, 105), clean);
    }

    [Fact]
    public void Sampling_interval_follows_visibility_and_alerts()
    {
        Assert.Equal(PerformanceSamplingPolicy.VisibleInterval, PerformanceSamplingPolicy.IntervalFor(true, false));
        Assert.Equal(PerformanceSamplingPolicy.VisibleInterval, PerformanceSamplingPolicy.IntervalFor(true, true));
        Assert.Equal(PerformanceSamplingPolicy.AlertInterval, PerformanceSamplingPolicy.IntervalFor(false, true));
        Assert.Null(PerformanceSamplingPolicy.IntervalFor(false, false));
    }

    [Fact]
    public void Temperature_alert_notice_is_normal_not_urgent_and_names_the_sensor()
    {
        var notice = PerformanceNotices.For(new TemperatureAlert(AlertSensor.Gpu, 87.4, 83));

        Assert.Equal("GPU 87 °C", notice.Title);
        Assert.Equal("Acima do limite de 83 °C", notice.Subtitle);
        Assert.False(notice.Urgent);
    }
}
