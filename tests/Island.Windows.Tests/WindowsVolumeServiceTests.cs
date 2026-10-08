using Island.Core.Models;
using Island.Windows.Audio;

namespace Island.Windows.Tests;

public sealed class WindowsVolumeServiceTests
{
    [Fact]
    public void Initialize_And_Dispose_DoNotThrow()
    {
        var service = new WindowsVolumeService();

        service.Initialize();
        service.Initialize();   // idempotent
        service.Dispose();
        service.Dispose();      // idempotent
    }

    [Fact]
    public void Current_IsWithinRange_AfterInitialize()
    {
        using var service = new WindowsVolumeService();
        service.Initialize();

        VolumeInfo v = service.Current;

        Assert.InRange(v.Level, 0, 100);
    }

    // Never call SetLevel/SetMuted on an initialized service here: it is attached to the machine's real
    // default render endpoint, so the test would change the developer's actual system volume (0, then 100).
    [Fact]
    public void SetLevel_And_SetMuted_WithOutOfRangeValues_DoNotThrow()
    {
        using var service = new WindowsVolumeService();   // not initialized: no endpoint, so nothing is written

        service.SetLevel(-50);
        service.SetLevel(500);
        service.SetMuted(false);

        Assert.InRange(service.Current.Level, 0, 100);
    }

    [Fact]
    public void Operations_BeforeInitialize_AreSafe()
    {
        using var service = new WindowsVolumeService();

        service.SetLevel(10);
        service.SetMuted(true);

        Assert.InRange(service.Current.Level, 0, 100);
    }
}
