using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Guardian;

/// <summary>
/// Starts and stops the hidden companion process (<c>DynamicIsland.exe --guardian &lt;pid&gt; &lt;stateFile&gt;</c>)
/// that outlives the island being killed during Angry. The same executable is reused, so publishing stays a single
/// binary. A named mutex allows one guardian per user session: when the reopened island tries to start its own, the
/// duplicate exits at once and the original keeps watching (it now polls the new island's state file).
/// </summary>
public sealed class AngryGuardianLauncher
{
    /// <summary>Per-user-session mutex name shared by the launcher and <see cref="GuardianHost"/>.</summary>
    public const string MutexName = @"Local\DynamicIsland.AngryGuardian";

    /// <summary>Minimum gap between start attempts, so a failing start cannot spawn processes in a loop.</summary>
    private static readonly TimeSpan StartCooldown = TimeSpan.FromSeconds(10);

    private readonly string _statePath;
    private readonly ILogger? _log;
    private readonly object _gate = new();
    private Process? _process;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private bool _disposed;

    public AngryGuardianLauncher(string statePath, ILogger? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        _statePath = Path.GetFullPath(statePath);
        _log = log;
    }

    /// <summary>Starts the guardian when none is running. Idempotent; safe to call on every session change.</summary>
    public void EnsureRunning()
    {
        lock (_gate)
        {
            if (_disposed || IsRunning()) return;
            if (_process is not null && !HasExited(_process)) return;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now - _lastAttempt < StartCooldown) return;
            _lastAttempt = now;

            _process?.Dispose();
            _process = StartGuardian();
        }
    }

    /// <summary>Stops the guardian we started, if any. Called when the session ends, on pause and on update.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_process is null) return;
            try
            {
                if (!HasExited(_process)) _process.Kill(entireProcessTree: false);
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "Could not stop the Angry guardian");
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }
    }

    /// <summary>
    /// True when a guardian process (ours or another instance's) holds the named mutex. On an unexpected error it
    /// answers true: starting a second guardian is worse than keeping the first one.
    /// </summary>
    public static bool IsRunning()
    {
        try
        {
            using var mutex = new Mutex(initiallyOwned: false, MutexName);
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                // The previous owner died without releasing it: nobody is guarding now.
                acquired = true;
            }

            if (!acquired) return true;
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            return false;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            return true;
        }
    }

    private Process? StartGuardian()
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            _log?.LogWarning("Guardian not started: could not find the app executable ({Path})", executable);
            return null;
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
        };
        startInfo.ArgumentList.Add("--guardian");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(_statePath);

        try
        {
            Process? process = Process.Start(startInfo);
            _log?.LogInformation("Angry guardian started (pid {Pid})", process?.Id);
            return process;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _log?.LogWarning(ex, "Could not start the Angry guardian");
            return null;
        }
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }
}
