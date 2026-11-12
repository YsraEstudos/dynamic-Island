using System.Diagnostics;
using System.Globalization;
using Island.Windows.Focus;
using Island.Windows.Pomodoro;

namespace Island.Windows.Guardian;

/// <summary>
/// The hidden guardian mode of the island executable: <c>DynamicIsland.exe --guardian &lt;parentPid&gt; &lt;stateFile&gt;</c>.
/// It is processed by <c>App.OnStartup</c> before any UI, logging or dependency injection, so it never opens a window.
/// <para>
/// Every <see cref="GuardianPolicy.PollInterval"/> it reads the live Angry state. When the island process is gone and the
/// session is still running, it (a) reopens the island and (b) meanwhile minimizes browsers on blocked sites itself,
/// using only the window title (no UI Automation). It exits when the state says inactive or the session end passed.
/// If the guardian is killed too, only SysGuard remains as a second line of defense.
/// </para>
/// </summary>
public static class GuardianHost
{
    private static readonly GuardianLog Log = new();

    /// <summary>True when the process was started in guardian mode.</summary>
    public static bool IsGuardianInvocation(IReadOnlyList<string> args) => GuardianPolicy.IsInvocation(args);

    /// <summary>Runs the guardian until the session ends and returns the process exit code. Blocks the caller.</summary>
    public static int Run(IReadOnlyList<string> args)
    {
        if (!GuardianPolicy.TryParseArguments(args, out int parentPid, out string statePath))
        {
            Log.Write("invalid arguments; usage: --guardian <parentPid> <stateFile>");
            return 2;
        }

        using var mutex = new Mutex(initiallyOwned: false, AngryGuardianLauncher.MutexName);
        bool ownsMutex;
        try
        {
            ownsMutex = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous guardian was killed: this process takes over the role.
            ownsMutex = true;
        }

        if (!ownsMutex)
        {
            Log.Write("another guardian already runs for this session; duplicate exiting");
            return 0;
        }

        try
        {
            Log.Write($"guardian started (island pid {parentPid}, state {statePath})");
            return Poll(parentPid, statePath);
        }
        catch (Exception ex)
        {
            Log.Write("guardian failed: " + ex);
            return 1;
        }
        finally
        {
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
    }

    private static int Poll(int parentPid, string statePath)
    {
        var store = new AngryLiveStateStore(statePath);
        using var titleGuard = new GuardianTitleGuard();
        int islandPid = parentPid;
        int missingReads = 0;
        DateTimeOffset relaunchNotBefore = DateTimeOffset.MinValue;

        while (true)
        {
            AngryLiveState? state = store.Read();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (state is null)
            {
                // Atomic writes never leave a gap; unreadable for three polls means there is nothing to guard.
                if (++missingReads >= GuardianPolicy.MissingStateTolerance)
                {
                    Log.Write("live state unreadable; exiting");
                    return 0;
                }
            }
            else
            {
                missingReads = 0;
                if (GuardianPolicy.ShouldExit(state, now))
                {
                    Log.Write(state.Active ? "Angry session ended; exiting" : "Angry session inactive; exiting");
                    return 0;
                }
            }

            bool islandAlive = GuardianPolicy.IsProcessAlive(islandPid);
            if (islandAlive)
            {
                // The island is running and applies its own guard (including UI Automation); ours is redundant.
                titleGuard.SetMode(SiteGuardMode.Off);
            }
            else if (state is not null)
            {
                titleGuard.SetMode(GuardianPolicy.TitleGuardMode(state, now));
                titleGuard.EvaluateForeground();

                if (GuardianPolicy.ShouldRelaunch(state, now) && now >= relaunchNotBefore)
                {
                    int? launched = RelaunchIsland();
                    if (launched is { } pid)
                    {
                        islandPid = pid;
                    }
                    else
                    {
                        relaunchNotBefore = now + GuardianPolicy.RelaunchRetry;
                    }
                }
            }

            Thread.Sleep(GuardianPolicy.PollInterval);
        }
    }

    /// <summary>Starts the island again (the same executable, no guardian arguments). Returns the new pid or null.</summary>
    private static int? RelaunchIsland()
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            Log.Write("cannot reopen the island: executable path not found (" + executable + ")");
            return null;
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
        };

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                Log.Write("cannot reopen the island: Process.Start returned null");
                return null;
            }

            Log.Write($"island killed during Angry: reopened (pid {process.Id.ToString(CultureInfo.InvariantCulture)})");
            return process.Id;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Log.Write("cannot reopen the island: " + ex.Message);
            return null;
        }
    }
}
