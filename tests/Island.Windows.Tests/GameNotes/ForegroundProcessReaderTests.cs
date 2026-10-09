using Island.Windows.GameNotes;
using Island.Windows.Interop;

namespace Island.Windows.Tests;

/// <summary>Runs the real Win32 reads once, against whatever window is in front. Nothing here changes the system.</summary>
public sealed class ForegroundProcessReaderTests
{
    [Fact]
    public void A_null_window_reads_as_an_ignored_process()
    {
        var reader = new ForegroundProcessReader();

        var process = reader.Read(IntPtr.Zero);

        Assert.True(process.IsOwnProcess);
        Assert.False(process.IsFullscreen);
        Assert.Null(process.ExePath);
    }

    [Fact]
    public void The_foreground_window_reads_without_throwing_and_its_path_is_consistent()
    {
        var reader = new ForegroundProcessReader();

        var process = reader.Read(NativeMethods.GetForegroundWindow());

        if (process.ExePath is null) return;   // access denied or the window closed: the reader degrades to no path
        Assert.True(Path.IsPathRooted(process.ExePath));
        Assert.Equal(Path.GetFileNameWithoutExtension(process.ExePath), process.ProcessName);
    }
}
