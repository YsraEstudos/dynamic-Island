namespace Island.Core.Pomodoro;

/// <summary>
/// Locks the focus phases of a pomodoro plan so they cannot be paused, reset or re-timed. A session covers the whole
/// plan: every focus is locked, every break is free, and the next focus locks again by itself. The only early exit is
/// typing an <see cref="UnlockPhrases"/> phrase. Without a plan, a session lasts for one focus.
/// </summary>
public sealed class AngryPomodoro : IDisposable
{
    private readonly PomodoroTimer _timer;
    private readonly Func<string> _phrasePicker;

    // Guards the fields below. Timer calls and events happen outside it.
    private readonly object _gate = new();
    private bool _session;
    private bool _locked;
    private string? _currentPhrase;
    private bool _disposed;

    public AngryPomodoro(PomodoroTimer timer, Func<string>? phrasePicker = null)
    {
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
        _phrasePicker = phrasePicker ?? (() => UnlockPhrases.Pick());

        _timer.Transitioned += OnTransitioned;
        _timer.Changed += OnChanged;
    }

    /// <summary>True while the timer is locked. Only ever true during a focus phase of the session.</summary>
    public bool IsLocked
    {
        get
        {
            lock (_gate) return _locked;
        }
    }

    /// <summary>True while a session is active, including its free breaks.</summary>
    public bool IsSessionActive
    {
        get
        {
            lock (_gate) return _session;
        }
    }

    /// <summary>The phrase stored for the active session; null when no session is active.</summary>
    public string? CurrentPhrase
    {
        get
        {
            lock (_gate) return _session ? _currentPhrase : null;
        }
    }

    /// <summary>Raised once for each change of <see cref="IsLocked"/> or <see cref="IsSessionActive"/>. Arbitrary thread, never under the internal lock.</summary>
    public event Action? LockChanged;

    /// <summary>
    /// Starts an angry session and locks the timer while it is in a focus phase. A fresh focus starts a plan of
    /// <paramref name="cycles"/> pomodoros; a paused single focus resumes without a plan; a plan already in progress
    /// keeps running. A running single focus keeps its remaining time.
    /// </summary>
    public void Engage(int cycles)
    {
        lock (_gate)
        {
            if (_disposed || _session) return;
        }

        if (!_timer.PlanActive)
        {
            if (_timer.Phase != PomodoroPhase.Focus) _timer.SetPhase(PomodoroPhase.Focus);
            if (!_timer.IsRunning && _timer.Remaining == _timer.PhaseDuration) _timer.StartPlan(cycles);
            else if (!_timer.IsRunning) _timer.Start();
        }
        else if (!_timer.IsRunning)
        {
            _timer.Start();
        }

        var lockNow = _timer.Phase == PomodoroPhase.Focus;
        if (lockNow) _timer.ControlsLocked = true;

        bool engaged;
        bool disposed;
        lock (_gate)
        {
            disposed = _disposed;
            engaged = !disposed && !_session;
            if (engaged)
            {
                _session = true;
                _locked = lockNow;
            }
        }

        // Disposed while we were engaging: Dispose already ran its cleanup, so undo the lock we just set.
        if (disposed)
        {
            _timer.ControlsLocked = false;
            return;
        }

        if (!engaged) return;

        LockChanged?.Invoke();

        // Focus can end between Start and the flag above. Its handlers ignored us then, so check for it now.
        if (lockNow && _timer.Phase != PomodoroPhase.Focus) Transition(session: _timer.PlanActive, locked: false);
    }

    /// <summary>Picks and stores a fresh phrase for the next unlock attempt. Works whether or not the session is active.</summary>
    public string NewPhrase()
    {
        var phrase = _phrasePicker();
        lock (_gate) _currentPhrase = phrase;
        return phrase;
    }

    /// <summary>
    /// Ends the session when <paramref name="typed"/> matches the current phrase, then resets the timer.
    /// Always true while no session is active.
    /// </summary>
    public bool TryUnlock(string? typed)
    {
        string? expected;
        lock (_gate)
        {
            if (!_session) return true;
            expected = _currentPhrase;
        }

        if (expected is null || !UnlockPhrases.IsMatch(expected, typed)) return false;

        // End the session before the reset: the timer refuses Reset while its controls are locked.
        Transition(session: false, locked: false);
        _timer.Reset();
        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _timer.Transitioned -= OnTransitioned;
        _timer.Changed -= OnChanged;
        Transition(session: false, locked: false);
    }

    // The timer has already switched phase, and started the next one itself when AutoStarted.
    private void OnTransitioned(PomodoroTransition transition)
    {
        bool session;
        lock (_gate) session = _session && !_disposed;
        if (!session) return;

        if (transition.PlanFinished || transition.Ended == PomodoroPhase.Prep || !transition.AutoStarted)
        {
            Transition(session: false, locked: false);
        }
        else if (transition.Next == PomodoroPhase.Focus)
        {
            // A break ended and the next focus started by itself: lock it again.
            Transition(session: true, locked: true);
        }
        else
        {
            // A focus ended and the plan carries on into its free break.
            Transition(session: true, locked: false);
        }
    }

    // Covers a Reset during a free break: the plan is gone, so there is nothing left to guard.
    private void OnChanged()
    {
        lock (_gate)
        {
            if (!_session || _locked || _disposed) return;
        }

        if (!_timer.PlanActive) Transition(session: false, locked: false);
    }

    /// <summary>
    /// Moves to the given state, keeps the timer's ControlsLocked in step with the lock, and raises
    /// <see cref="LockChanged"/> once when <see cref="IsLocked"/> or <see cref="IsSessionActive"/> changed.
    /// </summary>
    private void Transition(bool session, bool locked)
    {
        bool changed;
        lock (_gate)
        {
            changed = _session != session || _locked != locked;
            _session = session;
            _locked = locked;
            if (!session) _currentPhrase = null;
        }

        _timer.ControlsLocked = locked;
        if (changed) LockChanged?.Invoke();
    }
}
