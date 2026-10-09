using Island.Windows.Interop;

namespace Island.Windows.Input;

/// <summary>Registers a system-wide hotkey through its own message-only window thread.</summary>
public sealed class GlobalHotkey : IGlobalHotkey
{
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

    private const int HotkeyId = 1;

    private readonly uint _modifiers;
    private readonly uint _virtualKey;
    private readonly object _gate = new();
    private MessageWindowThread? _thread;
    private bool _registered;
    private bool _disposed;

    public GlobalHotkey(uint modifiers, uint virtualKey)
    {
        _modifiers = modifiers;
        _virtualKey = virtualKey;
    }

    /// <summary>Raised on the message-window thread (not the UI thread).</summary>
    public event Action? Pressed;

    /// <summary>
    /// Returns false if another app already owns the combination (never throws). Blocks until the window thread
    /// has attempted the registration. Calling it again while registered returns true.
    /// </summary>
    public bool Register()
    {
        lock (_gate)
        {
            if (_disposed) return false;
            if (_registered) return true;

            try
            {
                if (_thread is null)
                {
                    var thread = new MessageWindowThread("Hotkey", OnWindowMessage, logger: null);
                    if (!thread.Start())
                    {
                        thread.Dispose();
                        return false;
                    }
                    _thread = thread;
                }

                IntPtr hwnd = _thread.Handle;
                uint modifiers = _modifiers | NativeMethods.MOD_NOREPEAT;
                uint vk = _virtualKey;
                _registered = _thread.Invoke(() => NativeMethods.RegisterHotKey(hwnd, HotkeyId, modifiers, vk));
                return _registered;
            }
            catch (Exception)
            {
                // Documented contract: never throws. Treat any failure as "not registered".
                _registered = false;
                return false;
            }
        }
    }

    private IntPtr? OnWindowMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_HOTKEY || wParam != HotkeyId) return null;

        try
        {
            Pressed?.Invoke();
        }
        catch (Exception)
        {
            // A subscriber exception must not kill the message loop.
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        MessageWindowThread? thread;
        bool wasRegistered;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            thread = _thread;
            wasRegistered = _registered;
            _thread = null;
            _registered = false;
        }
        if (thread is null) return;

        if (wasRegistered)
        {
            IntPtr hwnd = thread.Handle;
            thread.Invoke(() => NativeMethods.UnregisterHotKey(hwnd, HotkeyId));
        }
        thread.Dispose();
    }
}
