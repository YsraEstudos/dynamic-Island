using Island.Core.Application;
using Island.Core.Models;
using Island.Core.Performance;

namespace Island.App.Composition;

/// <summary>
/// Connects the performance monitor to the island at startup: a temperature over its limit becomes a normal notice,
/// and sampling starts (it runs only while alerts are on or the widget is visible).
/// </summary>
public static class PerformanceWiring
{
    public static void Attach(PerformanceMonitor monitor, IslandCoordinator coordinator)
    {
        monitor.AlertRaised += alert => coordinator.Post(new IslandEvent.NoticeRaised(PerformanceNotices.For(alert)));
        monitor.Start();
    }
}
