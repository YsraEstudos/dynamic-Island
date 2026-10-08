using Island.Windows.Shell;

namespace Island.Windows.Tests;

public sealed class ShellTests
{
    [Fact]
    public void SingleInstanceGuard_SecondAcquireWithSameName_ReturnsNull()
    {
        string name = "island-test-" + Guid.NewGuid().ToString("N");

        using var first = SingleInstanceGuard.TryAcquire(name);
        using var second = SingleInstanceGuard.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void SingleInstanceGuard_ReacquiresAfterDispose()
    {
        string name = "island-test-" + Guid.NewGuid().ToString("N");

        var first = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(first);
        first!.Dispose();

        using var again = SingleInstanceGuard.TryAcquire(name);
        Assert.NotNull(again);
    }

    [Fact]
    public void StartupRegistration_IsEnabled_DoesNotThrow()
    {
        // Read-only on purpose: writes would change the real user's startup entries.
        _ = StartupRegistration.IsEnabled();
    }

    [Fact]
    public void OverlayWindowService_WithNullHandle_IsNoOp()
    {
        OverlayWindowService.ApplyOverlayStyles(IntPtr.Zero);
        OverlayWindowService.KeepOnTop(IntPtr.Zero);
    }
}
