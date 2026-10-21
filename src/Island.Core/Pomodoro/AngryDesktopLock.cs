using Island.Core.Abstractions;

namespace Island.Core.Pomodoro;

/// <summary>
/// Holds an Angry focus on one chosen Windows virtual desktop. While the Angry timer is locked (a focus phase of the
/// session), the chosen desktop is shown at once, and when the user switches away it is shown again
/// <see cref="ReturnDelay"/> later. Breaks, free time and sessions without a lock are not held.
/// </summary>
/// <remarks>
/// Windows has no public notification for a desktop change, so while a desktop is held the desktop is read every
/// <see cref="SamplePeriod"/>: one cheap call to the shell. Nothing is armed outside the hold. The sample and the
/// return timer stop when the hold lifts, and Dispose stops both.
/// </remarks>
public sealed class AngryDesktopLock : IDisposable
{
    /// <summary>How long a switched-away desktop is tolerated before the user is taken back.</summary>
    public static readonly TimeSpan ReturnDelay = TimeSpan.FromSeconds(2);

    /// <summary>Spacing of the desktop reads while a desktop is held.</summary>
    public static readonly TimeSpan SamplePeriod = TimeSpan.FromMilliseconds(500);

    private readonly AngryPomodoro _angry;
    private readonly IVirtualDesktopService _desktops;
    private readonly IIslandScheduler _scheduler;

    // Guards the fields below. Desktop switches run outside the gate.
    private readonly object _gate = new();
    private Guid? _target;
    private Guid? _held;
    // Bumped whenever a hold stops or changes, so a callback that was already dispatched does nothing.
    private int _generation;
    private IDisposable? _sample;
    private IDisposable? _return;
    private bool _disposed;

    public AngryDesktopLock(AngryPomodoro angry, IVirtualDesktopService desktops, IIslandScheduler scheduler)
    {
        _angry = angry ?? throw new ArgumentNullException(nameof(angry));
        _desktops = desktops ?? throw new ArgumentNullException(nameof(desktops));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

        _angry.LockChanged += Reconcile;
    }

    /// <summary>The chosen desktop, or null when no desktop is chosen.</summary>
    public Guid? TargetDesktopId
    {
        get
        {
            lock (_gate) return _target;
        }
    }

    /// <summary>True while a desktop is being held: a focus is locked, a desktop is chosen and desktops are available.</summary>
    public bool IsHolding
    {
        get
        {
            lock (_gate) return _held is not null;
        }
    }

    /// <summary>Chooses the desktop to hold; null releases it. While a focus is locked the change applies at once.</summary>
    public void SetTargetDesktop(Guid? desktopId)
    {
        lock (_gate)
        {
            if (_disposed || _target == desktopId) return;
            _target = desktopId;
        }

        Reconcile();
    }

    /// <summary>Saved form of a desktop id: 32 hex digits, or empty for none. Matches <see cref="ParseDesktopId"/>.</summary>
    public static string FormatDesktopId(Guid? desktopId) => desktopId?.ToString("N") ?? string.Empty;

    /// <summary>Reads an id written by <see cref="FormatDesktopId"/>; null when empty or malformed.</summary>
    public static Guid? ParseDesktopId(string? text) => Guid.TryParseExact(text, "N", out var id) ? id : null;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopLocked();
        }

        _angry.LockChanged -= Reconcile;
    }

    // Arbitrary thread (LockChanged). Starts, retargets or stops the hold to match the Angry lock.
    private void Reconcile()
    {
        Guid switchTo;
        lock (_gate)
        {
            if (_disposed) return;

            bool want = _target is not null && _angry.IsLocked && _desktops.IsAvailable;
            if (!want || _target is not { } target)
            {
                StopLocked();
                return;
            }

            if (_held == target) return;

            // A new hold, or a new target during a hold: the old return timer belongs to the old target.
            _held = target;
            _generation++;
            _return?.Dispose();
            _return = null;
            ArmSampleLocked(_generation);
            switchTo = target;
        }

        // The locked focus starts on the chosen desktop, not in ~2 s.
        Enforce(switchTo);
    }

    // Caller holds no lock. Shows the held desktop when the user is elsewhere.
    private void Enforce(Guid target)
    {
        Guid? current = _desktops.GetCurrentDesktopId();
        if (current is null || current == target) return;
        _desktops.SwitchTo(target);
    }

    // Scheduler callback, every SamplePeriod while held.
    private void Observe(int generation)
    {
        Guid target;
        lock (_gate)
        {
            if (_disposed || generation != _generation || _held is not { } held) return;
            target = held;
        }

        Guid? current = _desktops.GetCurrentDesktopId();

        lock (_gate)
        {
            if (_disposed || generation != _generation) return;

            if (current is { } now && now != target)
            {
                // One pending return at a time: a user who stays away is not counted again on every sample.
                _return ??= _scheduler.Schedule(ReturnDelay, () => Return(generation));
            }
            else if (current is not null)
            {
                // Back on the chosen desktop in time: no forced return.
                _return?.Dispose();
                _return = null;
            }

            ArmSampleLocked(generation);
        }
    }

    // Scheduler callback, ReturnDelay after the user left the held desktop.
    private void Return(int generation)
    {
        Guid target;
        lock (_gate)
        {
            if (_disposed || generation != _generation || _held is not { } held) return;
            _return = null;
            target = held;
        }

        Enforce(target);
    }

    // Caller holds the gate.
    private void ArmSampleLocked(int generation)
    {
        _sample?.Dispose();
        _sample = _scheduler.Schedule(SamplePeriod, () => Observe(generation));
    }

    // Caller holds the gate.
    private void StopLocked()
    {
        if (_held is null && _sample is null && _return is null) return;

        _held = null;
        _generation++;
        _sample?.Dispose();
        _sample = null;
        _return?.Dispose();
        _return = null;
    }
}
