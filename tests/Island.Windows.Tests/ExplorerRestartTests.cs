using Island.Windows.Interop;
using Island.Windows.Shell;

namespace Island.Windows.Tests;

public sealed class ExplorerRestartTests
{
    [Fact]
    public void RegisterWindowMessage_TaskbarCreated_ReturnsNonZero()
    {
        uint message = NativeMethods.RegisterWindowMessageW("TaskbarCreated");

        Assert.NotEqual(0u, message);
    }

    [Fact]
    public void RegisterWindowMessage_TaskbarCreated_IsStableAcrossCalls()
    {
        uint first = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        uint second = NativeMethods.RegisterWindowMessageW("TaskbarCreated");

        Assert.Equal(first, second);
        Assert.Equal(first, OverlayWindowService.TaskbarCreatedMessage);
    }
}
