using System.Runtime.InteropServices;
using System.Text;
using Island.Core.Clipboard;
using Island.Windows.Clipboard;
using Island.Windows.Interop;

namespace Island.Windows.Tests;

/// <summary>
/// These tests use the real system clipboard. Everything lives in one class so xunit runs them one at a time.
/// On a headless machine where the clipboard cannot be opened, the tests that need it return early.
/// </summary>
public sealed class WindowsClipboardServiceTests
{
    [Fact]
    public void Start_And_Dispose_DoNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            var service = new WindowsClipboardService();
            service.Start();
            service.Start();    // idempotent
            service.Dispose();
            service.Dispose();  // idempotent
        });

        Assert.Null(ex);
    }

    [Fact]
    public void SetText_DoesNotRaise_ItemCaptured()
    {
        using var service = new WindowsClipboardService();
        var captured = new List<ClipboardItem>();
        service.ItemCaptured += (_, item) => { lock (captured) captured.Add(item); };
        service.Start();

        string sentinel = "island-self-write-" + Guid.NewGuid();
        service.SetText(sentinel);
        Thread.Sleep(800);   // long enough for WM_CLIPBOARDUPDATE to arrive

        lock (captured)
        {
            Assert.DoesNotContain(captured, item => item.Text == sentinel);
        }
    }

    [Fact]
    public async Task ExternalTextCopy_RaisesItemCaptured_WithinTimeout()
    {
        using var service = new WindowsClipboardService();
        var seen = new TaskCompletionSource<ClipboardItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        string text = "island-external-" + Guid.NewGuid();
        service.ItemCaptured += (_, item) =>
        {
            if (item.Text == text) seen.TrySetResult(item);
        };
        service.Start();

        if (!TrySetClipboard((NativeMethods.CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + '\0'))))
            return;   // clipboard unavailable: nothing to verify

        Task finished = await Task.WhenAny(seen.Task, Task.Delay(TimeSpan.FromMilliseconds(1500)));
        Assert.Same(seen.Task, finished);
        ClipboardItem item = await seen.Task;
        Assert.Equal(ClipboardKind.Text, item.Kind);
    }

    [Fact]
    public void ExcludedFormat_IsIgnored_WhileNormalCopyIsCaptured()
    {
        using var service = new WindowsClipboardService();
        var captured = new List<ClipboardItem>();
        service.ItemCaptured += (_, item) => { lock (captured) captured.Add(item); };
        service.Start();

        // A password manager's copy: text plus the "exclude from monitoring" marker format.
        uint excludeFormat = NativeMethods.RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
        string secret = "island-secret-" + Guid.NewGuid();
        if (!TrySetClipboard(
                (NativeMethods.CF_UNICODETEXT, Encoding.Unicode.GetBytes(secret + '\0')),
                (excludeFormat, new byte[] { 0 })))
            return;   // clipboard unavailable

        Thread.Sleep(600);
        lock (captured)
        {
            Assert.DoesNotContain(captured, item => item.Text == secret);
        }

        // Control: a normal copy afterwards is captured, so the silence above is not just a dead listener.
        string normal = "island-normal-" + Guid.NewGuid();
        TrySetClipboard((NativeMethods.CF_UNICODETEXT, Encoding.Unicode.GetBytes(normal + '\0')));
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(1500);
        while (DateTime.UtcNow < deadline)
        {
            lock (captured)
            {
                if (captured.Any(item => item.Text == normal)) return;
            }
            Thread.Sleep(20);
        }
        Assert.Fail("The control copy was not captured within 1.5 s.");
    }

    /// <summary>Writes the given formats to the clipboard from the test thread, with no owner window.</summary>
    private static bool TrySetClipboard(params (uint Format, byte[] Data)[] formats)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                Thread.Sleep(20);
                continue;
            }

            try
            {
                if (!NativeMethods.EmptyClipboard()) return false;
                foreach (var (format, data) in formats)
                {
                    IntPtr memory = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)data.Length);
                    if (memory == IntPtr.Zero) return false;
                    IntPtr ptr = NativeMethods.GlobalLock(memory);
                    if (ptr == IntPtr.Zero)
                    {
                        NativeMethods.GlobalFree(memory);
                        return false;
                    }
                    Marshal.Copy(data, 0, ptr, data.Length);
                    NativeMethods.GlobalUnlock(memory);
                    if (NativeMethods.SetClipboardData(format, memory) == IntPtr.Zero)
                    {
                        NativeMethods.GlobalFree(memory);
                        return false;
                    }
                }
                return true;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }
        return false;
    }
}
