using System.Collections.Concurrent;
using Island.Core.Abstractions;
using Island.Core.Models;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Input;

/// <summary>
/// Observes the global Caps Lock key and publishes only toggle transitions.
/// The initial state is seeded silently, and repeated Start/Dispose calls are safe.
/// </summary>
public sealed class WindowsCapsLockNoticeSource : ISystemNoticeSource
{
    private readonly object _gate = new();
    private readonly IKeyboardHook _hook;
    private readonly Func<bool> _readCapsLock;
    private readonly ILogger? _logger;
    private readonly ConcurrentQueue<Notice> _pendingNotices = new();

    private CapsLockStateMachine? _state;
    private bool _started;
    private bool _disposed;
    private int _dispatching;

    public WindowsCapsLockNoticeSource(ILogger<WindowsCapsLockNoticeSource>? logger = null)
        : this(new WindowsKeyboardHook(logger), WindowsCapsLockStateReader.Read, logger)
    {
    }

    internal WindowsCapsLockNoticeSource(IKeyboardHook hook, Func<bool> readCapsLock, ILogger? logger = null)
    {
        _hook = hook ?? throw new ArgumentNullException(nameof(hook));
        _readCapsLock = readCapsLock ?? throw new ArgumentNullException(nameof(readCapsLock));
        _logger = logger;
    }

    public event EventHandler<Notice>? NoticeRaised;

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
            // The actual seed is read on the hook's message thread immediately before installation.
            _state = new CapsLockStateMachine(initiallyOn: false);
        }

        bool installed;
        try
        {
            installed = _hook.Start(OnKeyboardInput, ReadInitialState, ApplyInitialState);
        }
        catch (Exception ex)
        {
            installed = false;
            _logger?.LogWarning(ex, "Could not start the Caps Lock keyboard hook.");
        }

        bool disposeAfterStart;
        lock (_gate) disposeAfterStart = _disposed;
        if (!installed)
            _logger?.LogWarning("Could not install the Caps Lock keyboard hook.");
        if (disposeAfterStart)
            _hook.Dispose();
    }

    private bool ReadInitialState()
    {
        try { return _readCapsLock(); }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not read the initial Caps Lock state; using off as the silent baseline.");
            return false;
        }
    }

    private void ApplyInitialState(bool initialIsOn)
    {
        lock (_gate)
        {
            if (!_disposed && _state is not null)
                _state.Reconcile(initialIsOn);
        }
    }

    private void OnKeyboardInput(KeyboardInputEvent input)
    {
        CapsLockTransition? transition;
        lock (_gate)
        {
            if (!_started || _disposed || _state is null) return;

            // The hook callback is the source of truth after the silent seed. GetKeyState is thread-queue scoped
            // and may be stale on a background message thread, so it must not rebase every keydown edge.
            transition = _state.Process(input);
        }

        if (transition is { } changed)
            EnqueueNotice(CreateNotice(changed.IsOn));
    }

    private static Notice CreateNotice(bool isOn) => isOn
        ? new Notice("Caps Lock ativado", "Letras maiúsculas", "caps-on")
        : new Notice("Caps Lock desativado", "Letras minúsculas", "caps-off");

    private void EnqueueNotice(Notice notice)
    {
        _pendingNotices.Enqueue(notice);
        if (Interlocked.Exchange(ref _dispatching, 1) == 0)
        {
            try
            {
                ThreadPool.QueueUserWorkItem(static owner => ((WindowsCapsLockNoticeSource)owner!).DrainNotices(), this);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _dispatching, 0);
                _logger?.LogWarning(ex, "Could not dispatch a Caps Lock notice.");
            }
        }
    }

    private void DrainNotices()
    {
        while (_pendingNotices.TryDequeue(out Notice? notice))
        {
            lock (_gate)
            {
                if (_disposed) continue;
            }

            try { NoticeRaised?.Invoke(this, notice); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Caps Lock notice subscriber failed."); }
        }

        Volatile.Write(ref _dispatching, 0);
        if (!_pendingNotices.IsEmpty && Interlocked.Exchange(ref _dispatching, 1) == 0)
        {
            try
            {
                ThreadPool.QueueUserWorkItem(static owner => ((WindowsCapsLockNoticeSource)owner!).DrainNotices(), this);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _dispatching, 0);
                _logger?.LogWarning(ex, "Could not continue dispatching Caps Lock notices.");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pendingNotices.Clear();
        }

        try { _hook.Dispose(); }
        catch (Exception ex) { _logger?.LogDebug(ex, "Stopping the Caps Lock keyboard hook failed."); }
    }
}

internal static class CapsLockConstants
{
    public const uint VkCapital = 0x14;
}

internal readonly record struct KeyboardInputEvent(uint VirtualKey, bool IsKeyUp, bool IsInjected);

internal readonly record struct CapsLockTransition(bool IsOn);

internal sealed class CapsLockStateMachine
{
    private bool _isOn;
    private bool _capitalDown;

    public CapsLockStateMachine(bool initiallyOn) => _isOn = initiallyOn;

    public bool IsOn => _isOn;

    public void Reconcile(bool actualIsOn)
    {
        if (!_capitalDown) _isOn = actualIsOn;
    }

    public CapsLockTransition? Process(KeyboardInputEvent input)
    {
        if (input.VirtualKey != CapsLockConstants.VkCapital) return null;
        if (input.IsKeyUp)
        {
            _capitalDown = false;
            return null;
        }

        if (_capitalDown) return null;
        _capitalDown = true;
        _isOn = !_isOn;
        return new CapsLockTransition(_isOn);
    }
}
