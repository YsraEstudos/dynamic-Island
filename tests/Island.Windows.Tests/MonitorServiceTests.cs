using Island.Windows.Display;

namespace Island.Windows.Tests;

public sealed class MonitorServiceTests
{
    [Fact]
    public void GetMonitors_ReturnsAtLeastOne_WithPrimaryAtIndexZero()
    {
        var monitors = new MonitorService().GetMonitors();

        Assert.NotEmpty(monitors);
        Assert.True(monitors[0].IsPrimary);
        Assert.Equal(0, monitors[0].Index);
    }

    [Fact]
    public void GetMonitors_HasPositiveSizesAndDpiAtLeastOne()
    {
        foreach (var m in new MonitorService().GetMonitors())
        {
            Assert.True(m.Width > 0 && m.Height > 0);
            Assert.True(m.DpiScale >= 1.0);
        }
    }

    [Fact]
    public void GetMonitor_OutOfRange_FallsBackToPrimary()
    {
        var service = new MonitorService();

        var fallback = service.GetMonitor(999);

        Assert.True(fallback.IsPrimary);
        Assert.Equal(service.GetMonitors()[0], fallback);
        Assert.Equal(service.GetMonitors()[0], service.GetMonitor(-1));
    }
}
