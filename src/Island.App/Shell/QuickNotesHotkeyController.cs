using Island.Windows.Input;

namespace Island.App.Shell;

/// <summary>Owns the optional Ctrl+Alt+N registration and marshals capture requests onto the UI thread.</summary>
public sealed class QuickNotesHotkeyController : IDisposable
{
    private readonly Func<IGlobalHotkey> _createHotkey;
    private readonly Action _openForCapture;
    private readonly Action<Action> _dispatchToUi;
    private readonly Action<bool> _setHotkeyConflict;
    private IGlobalHotkey? _hotkey;
    private bool _isEnabled;
    private bool _isAvailable = true;
    private bool _disposed;

    public QuickNotesHotkeyController(
        Func<IGlobalHotkey> createHotkey,
        Action openForCapture,
        Action<Action> dispatchToUi,
        Action<bool> setHotkeyConflict)
    {
        _createHotkey = createHotkey ?? throw new ArgumentNullException(nameof(createHotkey));
        _openForCapture = openForCapture ?? throw new ArgumentNullException(nameof(openForCapture));
        _dispatchToUi = dispatchToUi ?? throw new ArgumentNullException(nameof(dispatchToUi));
        _setHotkeyConflict = setHotkeyConflict ?? throw new ArgumentNullException(nameof(setHotkeyConflict));
    }

    public bool IsEnabled => _isEnabled;
    public bool IsAvailable => _isAvailable;
    public bool HotkeyConflict => IsEnabled && !IsAvailable;

    public void SetEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isEnabled == enabled) return;

        bool previousConflict = HotkeyConflict;
        ReleaseHotkey();
        _isEnabled = enabled;

        if (!enabled)
        {
            _isAvailable = true;
        }
        else
        {
            try
            {
                _hotkey = _createHotkey();
                _hotkey.Pressed += OnPressed;
                _isAvailable = _hotkey.Register();
                if (!_isAvailable) ReleaseHotkey();
            }
            catch (Exception)
            {
                ReleaseHotkey();
                _isAvailable = false;
            }
        }

        if (previousConflict != HotkeyConflict || enabled)
            _setHotkeyConflict(HotkeyConflict);
    }

    public void Dispose()
    {
        if (_disposed) return;

        bool hadConflict = HotkeyConflict;
        _isEnabled = false;
        _isAvailable = true;
        _disposed = true;
        ReleaseHotkey();
        if (hadConflict) _setHotkeyConflict(false);
    }

    private void OnPressed() => _dispatchToUi(_openForCapture);

    private void ReleaseHotkey()
    {
        if (_hotkey is null) return;
        _hotkey.Pressed -= OnPressed;
        _hotkey.Dispose();
        _hotkey = null;
    }
}
