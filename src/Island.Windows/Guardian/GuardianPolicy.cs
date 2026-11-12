using System.Diagnostics;
using Island.Windows.Focus;
using Island.Windows.Pomodoro;

namespace Island.Windows.Guardian;

/// <summary>
/// The pure decisions of the guardian process. Kept free of I/O and Win32 so they can be tested: the host process
/// feeds it the live state and the clock and follows the answers.
/// </summary>
public static class GuardianPolicy
{
    /// <summary>How often the guardian checks the island and the live state.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Minimum gap between relaunch attempts, so a failing start cannot spawn processes in a tight loop.</summary>
    public static readonly TimeSpan RelaunchRetry = TimeSpan.FromSeconds(10);

    /// <summary>Consecutive unreadable snapshots tolerated before the guardian exits (the atomic write never leaves a gap).</summary>
    public const int MissingStateTolerance = 3;

    /// <summary>Invocation: <c>DynamicIsland.exe --guardian &lt;parentPid&gt; &lt;stateFile&gt;</c>.</summary>
    public static bool IsInvocation(IReadOnlyList<string> args) =>
        args.Count >= 3 && string.Equals(args[0], "--guardian", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses the guardian arguments. The path is made absolute so the working directory cannot change it.</summary>
    public static bool TryParseArguments(IReadOnlyList<string> args, out int parentPid, out string statePath)
    {
        parentPid = 0;
        statePath = string.Empty;
        if (!IsInvocation(args)) return false;
        if (!int.TryParse(args[1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out parentPid) || parentPid <= 0)
        {
            parentPid = 0;
            return false;
        }
        if (string.IsNullOrWhiteSpace(args[2])) return false;

        try { statePath = Path.GetFullPath(args[2]); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// True when the island process is gone but the Angry session is still running, so the guardian must reopen it.
    /// A session in a break counts too: quitting is not allowed during a break either, so the island should not stay dead.
    /// </summary>
    public static bool ShouldRelaunch(AngryLiveState? state, DateTimeOffset now) => state is not null && state.IsGuardable(now);

    /// <summary>True when there is nothing left to guard: no state, session ended, or its end time passed.</summary>
    public static bool ShouldExit(AngryLiveState? state, DateTimeOffset now)
    {
        if (state is null) return true;
        return !state.Active || state.SessionEndUtc <= now;
    }

    /// <summary>
    /// What the guardian blocks while the island is dead: every blocked site through a locked focus and through every
    /// break, except that Twitter/X and Instagram open inside the final minute of a running break — the same rule the
    /// island itself applies. The release is recomputed from the live state, so a stale snapshot cannot keep the sites
    /// open after that minute. A break waiting for manual Start stays fully blocked.
    /// </summary>
    public static SiteGuardMode TitleGuardMode(AngryLiveState state, DateTimeOffset now) =>
        state.IsBreakReleaseNow(now) ? SiteGuardMode.BreakRelease : SiteGuardMode.All;

    /// <summary>True when a PID belongs to a running process. A recycled PID can only prolong the guardian, never weaken the guard.</summary>
    public static bool IsProcessAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
