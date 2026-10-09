using Island.App.Widgets;
using Island.Core.Capture;
using Island.Windows.Input;

namespace Island.App.Shell;

/// <summary>
/// Global Ctrl+Alt+P (screenshot) and Ctrl+Alt+R (start or stop recording). Each key registers on its own, so a conflict on
/// one does not disable the other. Hotkey callbacks arrive on the hotkey thread and are sent to the UI thread first.
/// </summary>
public sealed class CaptureHotkeys : ICaptureShortcutStatus, IDisposable
{
    public const uint PrintKey = 0x50;  // P
    public const uint RecordKey = 0x52; // R

    private readonly CaptureController _capture;
    private readonly Func<uint, IGlobalHotkey> _createHotkey;
    private readonly Action<Action> _dispatchToUi;
    private IGlobalHotkey? _print;
    private IGlobalHotkey? _record;
    private bool _started;
    private bool _disposed;

    public CaptureHotkeys(CaptureController capture, Func<uint, IGlobalHotkey> createHotkey, Action<Action> dispatchToUi)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _createHotkey = createHotkey ?? throw new ArgumentNullException(nameof(createHotkey));
        _dispatchToUi = dispatchToUi ?? throw new ArgumentNullException(nameof(dispatchToUi));
    }

    public bool PrintAvailable { get; private set; } = true;

    public bool RecordAvailable { get; private set; } = true;

    public event Action? AvailabilityChanged;

    /// <summary>Registers both keys. Call once, on the UI thread. Failures are reported through the availability properties.</summary>
    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;

        _print = Register(PrintKey, () => _ = _capture.TakeScreenshotAsync());
        _record = Register(RecordKey, () => _ = _capture.ToggleRecordingAsync());
        PrintAvailable = _print is not null;
        RecordAvailable = _record is not null;
        AvailabilityChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _print?.Dispose();
        _record?.Dispose();
        _print = null;
        _record = null;
    }

    /// <summary>Returns the registered hotkey, or null when Windows refuses the combination (already taken).</summary>
    private IGlobalHotkey? Register(uint key, Action action)
    {
        try
        {
            IGlobalHotkey hotkey = _createHotkey(key);
            hotkey.Pressed += () => _dispatchToUi(action);
            if (hotkey.Register()) return hotkey;

            hotkey.Dispose();
            return null;
        }
        catch (Exception)
        {
            // GlobalHotkey never throws by contract; treat anything unexpected as "not available".
            return null;
        }
    }
}
