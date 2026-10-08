using System.Diagnostics;
using Island.Core.Pomodoro;
using Island.Windows.Audio;
using Island.Windows.Clipboard;
using Island.Windows.Display;
using Island.Windows.Focus;
using Island.Windows.Input;
using Island.Windows.Interop;
using Island.Windows.Media;
using NAudio.CoreAudioApi;
using Windows.Media.Control;

namespace Island.Windows.Tests;

/// <summary>
/// Lifecycle and leak checks. Repeated construct / start / dispose cycles must not throw, and the process handle
/// count must not grow across repeated hotkey, message-window and guard cycles. Services that need an audio device
/// or a media session return early when none is present, as the other tests in this project do.
/// </summary>
public sealed class ResourceLeakTests
{
    private const int LifecycleCycles = 200;
    private const int HandleCycles = 100;
    private const int MaxHandleGrowth = 50;

    // Ctrl+Alt+Shift+F23: distinct from GlobalHotkeyTests (F24) and from the app's Ctrl+Alt+V.
    private const uint Modifiers = GlobalHotkey.ModControl | GlobalHotkey.ModAlt | GlobalHotkey.ModShift;
    private const uint VkF23 = 0x86;

    [Fact]
    public void WindowsVolumeService_InitializeAndDispose_RepeatedCycles_DoNotThrow()
    {
        if (!HasDefaultRenderEndpoint()) return;   // no audio device: nothing to attach to

        // Initialize only registers callbacks and reads the level. It never writes the system volume.
        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < LifecycleCycles; i++)
            {
                var service = new WindowsVolumeService();
                service.Initialize();
                service.Initialize();   // idempotent
                service.Dispose();
                service.Dispose();      // idempotent
            }
        });

        Assert.Null(ex);
    }

    [Fact]
    public async Task WindowsMediaService_InitializeAndDispose_RepeatedCycles_DoNotThrow()
    {
        if (!await HasMediaSessionManagerAsync()) return;   // no system media transport controls

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        for (int i = 0; i < LifecycleCycles; i++)
        {
            var service = new WindowsMediaService();
            await service.InitializeAsync(cts.Token);
            service.Dispose();
            service.Dispose();   // idempotent
        }
    }

    [Fact]
    public void WindowsClipboardService_StartAndDispose_RepeatedCycles_DoNotThrow()
    {
        // Start only creates the listener window. Nothing is written to the clipboard.
        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < LifecycleCycles; i++)
            {
                var service = new WindowsClipboardService();
                service.Start();
                service.Start();    // idempotent
                service.Dispose();
                service.Dispose();  // idempotent
            }
        });

        Assert.Null(ex);
    }

    [Fact]
    public void FullscreenDetector_StartAndDispose_RepeatedCycles_DoNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < LifecycleCycles; i++)
            {
                var detector = new FullscreenDetector();
                detector.Start();
                detector.Start();     // idempotent
                detector.Dispose();
                detector.Dispose();   // idempotent
                detector.Evaluate();  // after Dispose: returns the last state and raises nothing
            }
        });

        Assert.Null(ex);
    }

    [Fact]
    public void GlobalHotkey_RegisterAndDispose_RepeatedCycles_DoNotThrow()
    {
        // Register may return false (another app owns the combination, or no session). That is not a failure here.
        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < LifecycleCycles; i++)
            {
                var hotkey = new GlobalHotkey(Modifiers, VkF23);
                hotkey.Register();
                hotkey.Dispose();
                hotkey.Dispose();   // idempotent
            }
        });

        Assert.Null(ex);
    }

    [Fact]
    public void ForegroundSiteGuard_SetActiveAndDispose_RepeatedCycles_DoNotThrow()
    {
        if (!ForegroundIsSafeToActivate()) return;

        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < LifecycleCycles; i++)
            {
                var guard = new ForegroundSiteGuard();
                guard.SetActive(true);
                guard.SetActive(true);   // idempotent
                guard.Dispose();
                guard.Dispose();         // idempotent
                guard.SetActive(true);   // after Dispose: ignored
            }
        });

        Assert.Null(ex);
    }

    [Fact]
    public void HandleCount_DoesNotGrow_AcrossHotkeyMessageWindowAndGuardCycles()
    {
        bool activateGuard = ForegroundIsSafeToActivate();

        // One warm-up pass allocates lazily created state (first window class, thread pool). That is not a leak.
        RunHandleCycle(activateGuard);
        int before = CurrentHandleCount();

        for (int i = 0; i < HandleCycles; i++)
            RunHandleCycle(activateGuard);

        int growth = CurrentHandleCount() - before;
        Assert.True(growth < MaxHandleGrowth,
            $"Process handle count grew by {growth} across {HandleCycles} hotkey/window/guard cycles (limit {MaxHandleGrowth}).");
    }

    /// <summary>One hotkey, one message-window thread and one guard, each created and disposed.</summary>
    private static void RunHandleCycle(bool activateGuard)
    {
        var hotkey = new GlobalHotkey(Modifiers, VkF23);
        hotkey.Register();
        hotkey.Dispose();

        var window = new MessageWindowThread("HandleProbe", onMessage: null, logger: null);
        window.Start();
        window.Dispose();

        var guard = new ForegroundSiteGuard();
        if (activateGuard) guard.SetActive(true);
        guard.Dispose();
    }

    private static int CurrentHandleCount()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var process = Process.GetCurrentProcess();
        return process.HandleCount;
    }

    /// <summary>
    /// SetActive(true) minimizes the foreground window when it is a browser on a blocked site. The activation cycles
    /// run only when the current foreground window is not on one, so the test never minimizes a real window.
    /// </summary>
    private static bool ForegroundIsSafeToActivate()
    {
        string title = NativeMethods.GetWindowTitle(NativeMethods.GetForegroundWindow());
        return BlockedSites.Match(title) is null;
    }

    private static bool HasDefaultRenderEndpoint()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return true;
        }
        catch (Exception)
        {
            return false;   // no audio service or no render endpoint
        }
    }

    private static async Task<bool> HasMediaSessionManagerAsync()
    {
        try
        {
            await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
            return true;
        }
        catch (Exception)
        {
            return false;   // media transport controls unavailable in this session
        }
    }
}
