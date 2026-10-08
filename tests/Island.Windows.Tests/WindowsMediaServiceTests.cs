using Island.Windows.Media;

namespace Island.Windows.Tests;

public sealed class WindowsMediaServiceTests
{
    [Fact]
    public async Task InitializeAndDispose_DoNotThrow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var service = new WindowsMediaService();

        await service.InitializeAsync(cts.Token);
        service.Dispose();
        service.Dispose();   // idempotent
    }

    [Fact]
    public async Task Controls_WithoutSession_DoNotThrow()
    {
        using var service = new WindowsMediaService();

        await service.PlayPauseAsync();
        await service.NextAsync();
        await service.PreviousAsync();
        await service.SeekAsync(TimeSpan.FromSeconds(5));
    }
}
