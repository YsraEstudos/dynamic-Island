namespace Island.Core.Performance;

/// <summary>How often to sample. Nothing needs data when the widget is hidden and alerts are off: then no timer runs.</summary>
public static class PerformanceSamplingPolicy
{
    public static readonly TimeSpan VisibleInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan AlertInterval = TimeSpan.FromSeconds(3);

    /// <returns>Delay between samples, or null when sampling must stop.</returns>
    public static TimeSpan? IntervalFor(bool widgetVisible, bool alertsEnabled) =>
        widgetVisible ? VisibleInterval : alertsEnabled ? AlertInterval : null;
}
