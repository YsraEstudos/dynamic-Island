namespace Island.Core.Pomodoro;

/// <summary>
/// Locks a focus session so it cannot be paused, reset or re-timed. The only early exit is typing an
/// <see cref="UnlockPhrases"/> phrase; the lock also ends when the focus phase completes.
/// </summary>
public sealed class AngryPomodoro : IDisposable
{
    private readonly PomodoroTimer _timer;
    private readonly Func<string> _phrasePicker;

    // Guards the fields below. Timer calls and events happen outside it.
    private readonly object _gate = new();
    private bool _locked;
    private string? _currentPhrase;
    private bool _disposed;

    public AngryPomodoro(PomodoroTimer timer, Func<string>? phrasePicker = null)
    {
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
        _phrasePicker = phrasePicker ?? (() => UnlockPhrases.Pick());

        _timer.PhaseCompleted += OnPhaseCompleted;
        _timer.Changed += OnChanged;
    }

    public bool IsLocked
    {
        get
        {
            lock (_gate) return _locked;
        }
    }

    /// <summary>The phrase of the active unlock attempt; null while unlocked.</summary>
    public string? CurrentPhrase
    {
        get
        {
            lock (_gate) return _locked ? _currentPhrase : null;
        }
    }

    /// <summary>Raised each time <see cref="IsLocked"/> flips. Arbitrary thread, never under the internal lock.</summary>
    public event Action? LockChanged;

    /// <summary>Starts focus if needed and locks the timer. A running focus keeps its remaining time.</summary>
    public void Engage()
    {
        lock (_gate)
        {
            if (_locked || _disposed) return;
        }

        if (_timer.Phase != PomodoroPhase.Focus) _timer.SetPhase(PomodoroPhase.Focus);
        if (!_timer.IsRunning) _timer.Start();
        _timer.ControlsLocked = true;

        bool engaged;
        bool disposed;
        lock (_gate)
        {
            disposed = _disposed;
            engaged = !disposed && !_locked;
            if (engaged) _locked = true;
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
        if (_timer.Phase != PomodoroPhase.Focus) Disarm();
    }

    /// <summary>Picks and stores a fresh phrase for the next unlock attempt. Works whether or not the session is locked.</summary>
    public string NewPhrase()
    {
        var phrase = _phrasePicker();
        lock (_gate) _currentPhrase = phrase;
        return phrase;
    }

    /// <summary>Ends the lock when <paramref name="typed"/> matches the current phrase. Always true while unlocked.</summary>
    public bool TryUnlock(string? typed)
    {
        string? expected;
        lock (_gate)
        {
            if (!_locked) return true;
            expected = _currentPhrase;
        }

        if (expected is null || !UnlockPhrases.IsMatch(expected, typed)) return false;

        // Disarm before the reset: the timer refuses Reset while its controls are locked.
        if (Disarm()) _timer.Reset();
        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _timer.PhaseCompleted -= OnPhaseCompleted;
        _timer.Changed -= OnChanged;
        Disarm();
    }

    private void OnPhaseCompleted(PomodoroPhase phase)
    {
        if (phase == PomodoroPhase.Focus) Disarm();
    }

    // The timer switches to Break before raising Changed for a natural completion, so this catches it too.
    private void OnChanged()
    {
        bool locked;
        lock (_gate) locked = _locked;

        if (locked && _timer.Phase == PomodoroPhase.Break) Disarm();
    }

    /// <summary>Ends the lock if it is active. Returns true only for the call that actually ended it.</summary>
    private bool Disarm()
    {
        lock (_gate)
        {
            if (!_locked) return false;
            _locked = false;
            _currentPhrase = null;
        }

        _timer.ControlsLocked = false;
        LockChanged?.Invoke();
        return true;
    }
}
