using System.Diagnostics;
using Island.Core.Abstractions;
using Island.Windows.Audio;

namespace Island.Windows.Tests;

/// <summary>
/// Lifecycle and leak checks for the app mixer. The tests never change a session's volume or mute: they only start,
/// meter and dispose, so the developer's real app volumes are left alone. They pass with no audio device as well.
/// </summary>
public sealed class WindowsAudioMixerServiceTests
{
    private const int LifecycleCycles = 20;
    private const int HandleCycles = 30;
    private const int MaxHandleGrowth = 50;

    [Fact]
    public void Sessions_are_empty_before_Start()
    {
        using var mixer = new WindowsAudioMixerService();

        Assert.Empty(mixer.Sessions);
    }

    [Fact]
    public void Start_and_Dispose_repeated_cycles_do_not_throw()
    {
        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < LifecycleCycles; i++)
            {
                var mixer = new WindowsAudioMixerService();
                mixer.Start();
                mixer.Start();                  // idempotent
                using (mixer.BeginMetering())
                {
                    _ = mixer.Sessions;
                }
                mixer.Dispose();
                mixer.Dispose();                // idempotent
                using (mixer.BeginMetering())   // after Dispose: a no-op scope
                {
                }
            }
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Sessions_read_from_any_thread_while_the_thread_starts()
    {
        using var mixer = new WindowsAudioMixerService();
        mixer.Start();

        // Reads are lock-protected snapshots; the mixer thread may still be attaching.
        for (int i = 0; i < 100; i++)
        {
            Assert.NotNull(mixer.Sessions);
        }
    }

    [Fact]
    public void Handle_count_does_not_grow_across_start_and_dispose_cycles()
    {
        RunCycle();   // warm-up: first use loads the audio stack and thread pool

        // The handle count is process-wide and other test classes run in parallel, so one run can be inflated by
        // unrelated handles. A real leak grows on every attempt; noise does not.
        int growth = int.MaxValue;
        for (int attempt = 0; attempt < 3 && growth >= MaxHandleGrowth; attempt++)
        {
            int before = CurrentHandleCount();
            for (int i = 0; i < HandleCycles; i++) RunCycle();
            growth = Math.Min(growth, CurrentHandleCount() - before);
        }

        Assert.True(growth < MaxHandleGrowth,
            $"Process handle count grew by at least {growth} across {HandleCycles} mixer start/dispose cycles on every attempt (limit {MaxHandleGrowth}).");
    }

    [Fact]
    public void ProcessIdentity_resolves_the_running_process()
    {
        string? path = ProcessIdentity.TryGetExecutablePath(Environment.ProcessId);
        ProcessIdentity identity = ProcessIdentity.Resolve(Environment.ProcessId);

        Assert.NotNull(path);
        Assert.EndsWith(".exe", path, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrEmpty(identity.ProcessName));
    }

    [Fact]
    public void ProcessIdentity_of_an_unknown_process_is_empty_not_an_error()
    {
        Assert.Null(ProcessIdentity.TryGetExecutablePath(0));
        Assert.Null(ProcessIdentity.TryGetExecutablePath(-1));
    }

    [Fact]
    public void ForegroundProcess_returns_a_process_id_or_zero()
    {
        Assert.True(ForegroundProcess.CurrentId() >= 0);
    }

    private static void RunCycle()
    {
        var mixer = new WindowsAudioMixerService();
        mixer.Start();
        using (mixer.BeginMetering())
        {
        }
        mixer.Dispose();
    }

    private static int CurrentHandleCount()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var process = Process.GetCurrentProcess();
        return process.HandleCount;
    }
}
