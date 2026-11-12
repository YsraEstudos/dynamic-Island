using System.Diagnostics;
using Island.Core.Abstractions;
using Island.Core.Pomodoro;
using Island.Windows.Guardian;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Pomodoro;

/// <summary>
/// Keeps <see cref="AngryLiveStateStore"/> in step with the running Angry session: one write on every lock/session
/// change and on every structural timer change (phase, running state, plan cycle), plus one every 5 s while a session
/// is active so the file never gets stale. It also starts and stops the hidden guardian process with the session.
/// Writes and the guardian are best-effort: a failure is logged and never affects the session.
/// </summary>
public sealed class AngryLiveStateWriter : IDisposable
{
    /// <summary>How often the state is rewritten while a session is active.</summary>
    public static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(5);

    private readonly AngryPomodoro _angry;
    private readonly PomodoroTimer _timer;
    private readonly AngryLiveStateStore _store;
    private readonly IIslandScheduler _scheduler;
    private readonly AngryGuardianLauncher? _guardian;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<bool> _phoneBlockEnabled;
    private readonly ILogger? _log;

    private readonly object _gate = new();
    private IDisposable? _periodic;
    private string _signature = string.Empty;
    private bool _started;
    private bool _disposed;

    public AngryLiveStateWriter(AngryPomodoro angry, PomodoroTimer timer, AngryLiveStateStore store,
        IIslandScheduler scheduler, AngryGuardianLauncher? guardian = null, Func<bool>? phoneBlockEnabled = null,
        Func<DateTimeOffset>? clock = null, ILogger? log = null)
    {
        _angry = angry ?? throw new ArgumentNullException(nameof(angry));
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _guardian = guardian;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _phoneBlockEnabled = phoneBlockEnabled ?? (() => false);
        _log = log;
    }

    /// <summary>
    /// Starts writing the live state. Call it after a restored session was put back (or with no session) and before the
    /// weekly schedule runs. Writes the current snapshot right away.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _started) return;
            _started = true;
        }

        _angry.LockChanged += OnLockChanged;
        _timer.Changed += OnTimerChanged;

        // The first snapshot also replaces a stale file left by a previous run (it is ignored, then rewritten).
        Flush();
        if (_angry.IsSessionActive)
        {
            EnsurePeriodic();
            _guardian?.EnsureRunning();
        }
    }

    /// <summary>Writes the current snapshot now. Never throws.</summary>
    public void Flush()    {
        try
        {
            AngryResumeState? session = _angry.CaptureSession();
            bool phoneBlockEnabled = _phoneBlockEnabled();
            AngryLiveState state = session is null
                ? AngryLiveState.Inactive(_timer.Capture(), _clock(), phoneBlockEnabled)
                : AngryLiveState.FromSession(session, _clock(), phoneBlockEnabled);
            _store.Save(state);
            _signature = BuildSignature();
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Could not flush the live Angry state");
        }
    }

    /// <summary>Stops the guardian process without stopping the live state writes (used before an update replaces the exe).</summary>
    public void StopGuardian() => _guardian?.Stop();

    /// <summary>Makes sure the guardian is running again (used when an update attempt failed and the session continues).</summary>
    public void EnsureGuardian()
    {
        if (_angry.IsSessionActive) _guardian?.EnsureRunning();
    }

    private void OnLockChanged()
    {
        try
        {
            Flush();
            if (_angry.IsSessionActive)
            {
                EnsurePeriodic();
                _guardian?.EnsureRunning();
            }
            else
            {
                StopPeriodic();
                _guardian?.Stop();
            }
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Live Angry state handler failed");
        }
    }

    // Timer ticks are frequent (about once per second); only a structural change is written at once. The 5 s loop
    // takes care of the changing remaining time.
    private void OnTimerChanged()
    {
        try
        {
            if (!_angry.IsSessionActive) return;
            string signature = BuildSignature();
            if (!string.Equals(signature, _signature, StringComparison.Ordinal)) Flush();
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Live Angry state handler failed");
        }
    }

    private string BuildSignature() => string.Join('|',
        _angry.IsSessionActive, _angry.IsLocked, _angry.IsBreakReleaseOpen, _timer.Phase, _timer.IsRunning,
        _timer.Cycle, _timer.TotalCycles, _timer.IsAwaitingNextPhase);

    private void EnsurePeriodic()
    {
        lock (_gate)
        {
            if (_disposed || _periodic is not null) return;
            _periodic = _scheduler.Schedule(SaveInterval, OnPeriodic);
        }
    }

    private void StopPeriodic()
    {
        IDisposable? periodic;
        lock (_gate)
        {
            periodic = _periodic;
            _periodic = null;
        }
        periodic?.Dispose();
    }

    private void OnPeriodic()
    {
        bool active;
        lock (_gate)
        {
            if (_disposed) return;
            _periodic = null;
        }

        Flush();
        active = _angry.IsSessionActive;
        if (active) EnsurePeriodic();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _angry.LockChanged -= OnLockChanged;
        _timer.Changed -= OnTimerChanged;
        StopPeriodic();
        _guardian?.Stop();
        Trace.Flush();
    }
}
