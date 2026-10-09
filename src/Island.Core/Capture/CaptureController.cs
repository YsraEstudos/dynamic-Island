using Island.Core.Abstractions;
using Island.Core.Models;

namespace Island.Core.Capture;

/// <summary>
/// Print and recording use cases for the Capture widget. Saves files into one folder, keeps the recording state and
/// reports results as island notices. Backend calls run through <see cref="IScreenCaptureService"/>, so this class has
/// no Windows code and is covered by unit tests. Safe to call from the UI thread; <see cref="Changed"/> may be raised
/// from any thread (the widget marshals it).
/// </summary>
public sealed class CaptureController : IDisposable
{
    /// <summary>Notice glyph keys understood by the island's notice view.</summary>
    public const string PrintGlyph = "capture-print";
    public const string RecordGlyph = "capture-record";

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly IScreenCaptureService _capture;
    private readonly Action<Notice> _notify;
    private readonly TimeProvider _clock;
    private readonly Func<string, bool> _fileExists;
    private readonly RecordingStateMachine _state = new();
    private string? _lastScreenshot;
    private string? _recordingPath;
    private DateTimeOffset _recordingStarted;
    private bool _disposed;

    /// <param name="capture">Backend that takes screenshots and records video.</param>
    /// <param name="folder">Folder that receives every capture (created by the backend on first write).</param>
    /// <param name="notify">Shows a temporary island notice.</param>
    /// <param name="latestScreenshotPath">The newest existing screenshot, so the widget can show it after a restart.</param>
    /// <param name="clock">Time source; defaults to the system clock.</param>
    /// <param name="fileExists">Name check used to avoid overwriting; defaults to File.Exists.</param>
    public CaptureController(
        IScreenCaptureService capture,
        string folder,
        Action<Notice> notify,
        string? latestScreenshotPath = null,
        TimeProvider? clock = null,
        Func<string, bool>? fileExists = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        Folder = !string.IsNullOrWhiteSpace(folder) ? folder : throw new ArgumentException("A folder is required.", nameof(folder));
        _notify = notify ?? throw new ArgumentNullException(nameof(notify));
        _lastScreenshot = latestScreenshotPath;
        _clock = clock ?? TimeProvider.System;
        _fileExists = fileExists ?? File.Exists;
        _capture.RecordingEnded += OnBackendRecordingEnded;
    }

    /// <summary>Raised after any state change (phase, last screenshot). May be raised from any thread.</summary>
    public event Action? Changed;

    public string Folder { get; }

    public RecordingPhase Phase
    {
        get
        {
            lock (_gate) return _state.Phase;
        }
    }

    /// <summary>Time since the recording started, or zero when none is running.</summary>
    public TimeSpan RecordingElapsed
    {
        get
        {
            lock (_gate)
            {
                if (!_state.IsRecording) return TimeSpan.Zero;
                return _clock.GetUtcNow() - _recordingStarted;
            }
        }
    }

    /// <summary>Full path of the newest screenshot taken in this folder, or null when there is none.</summary>
    public string? LastScreenshotPath
    {
        get
        {
            lock (_gate) return _lastScreenshot;
        }
    }

    /// <summary>Takes one screenshot. Allowed while recording. Returns true when the file was saved.</summary>
    public async Task<bool> TakeScreenshotAsync()
    {
        string path;
        lock (_gate)
        {
            if (_disposed) return false;
            path = NewPath(CaptureNaming.ScreenshotFileName(_clock.GetLocalNow().DateTime));
        }

        bool saved = await SafeAsync(() => _capture.SaveScreenshotAsync(path));
        if (!saved)
        {
            _notify(new Notice("Print não salvo", "Veja os logs", PrintGlyph));
            return false;
        }

        lock (_gate) _lastScreenshot = path;
        RaiseChanged();
        _notify(new Notice("Print salvo", "Pasta Capturas", PrintGlyph));
        return true;
    }

    /// <summary>Starts a recording when idle, or stops the current one. Returns true when the call changed the recording.</summary>
    public Task<bool> ToggleRecordingAsync()
    {
        bool recording;
        lock (_gate) recording = _state.IsRecording;
        return recording ? StopRecordingAsync() : StartRecordingAsync();
    }

    public async Task<bool> StartRecordingAsync()
    {
        string path;
        lock (_gate)
        {
            if (_disposed || !_state.BeginStart()) return false;
            path = NewPath(CaptureNaming.RecordingFileName(_clock.GetLocalNow().DateTime));
        }
        RaiseChanged();

        bool started = await SafeAsync(() => _capture.StartRecordingAsync(path));
        lock (_gate)
        {
            if (started)
            {
                _state.StartSucceeded();
                _recordingPath = path;
                _recordingStarted = _clock.GetUtcNow();
            }
            else
            {
                _state.StartFailed();
            }
        }
        RaiseChanged();

        _notify(started
            ? new Notice("Gravação iniciada", "Pare com Ctrl+Alt+R", RecordGlyph)
            : new Notice("Gravação não iniciou", "Veja os logs", RecordGlyph));
        return started;
    }

    public async Task<bool> StopRecordingAsync()
    {
        TimeSpan length;
        lock (_gate)
        {
            if (_disposed || !_state.BeginStop()) return false;
            length = _clock.GetUtcNow() - _recordingStarted;
        }
        RaiseChanged();

        await SafeAsync(() => _capture.StopRecordingAsync());
        lock (_gate)
        {
            _state.StopCompleted();
            _recordingPath = null;
        }
        RaiseChanged();

        _notify(new Notice("Gravação salva", $"{ElapsedTime.Format(length)} em Capturas", RecordGlyph));
        return true;
    }

    /// <summary>Stops a running recording before the app exits. Waits a few seconds so the file is finalized.</summary>
    public void Dispose()
    {
        bool stopNeeded;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            stopNeeded = _state.IsRecording;
        }

        _capture.RecordingEnded -= OnBackendRecordingEnded;
        if (!stopNeeded) return;

        try { _capture.StopRecordingAsync().Wait(StopTimeout); }
        catch (AggregateException) { /* Logged by the backend; nothing else can be done during shutdown. */ }
    }

    private void OnBackendRecordingEnded(object? sender, EventArgs e)
    {
        bool changed;
        lock (_gate)
        {
            changed = _state.EndedByBackend();
            _recordingPath = null;
        }
        if (!changed) return;

        RaiseChanged();
        _notify(new Notice("Gravação interrompida", "Veja os logs", RecordGlyph));
    }

    /// <summary>Builds a path in the folder for a file name that does not exist yet. Call under the lock.</summary>
    private string NewPath(string fileName)
    {
        string unique = CaptureNaming.MakeUnique(fileName, candidate => _fileExists(Path.Combine(Folder, candidate)));
        return Path.Combine(Folder, unique);
    }

    /// <summary>Backend calls report failure by value; an unexpected exception is treated as a failure too.</summary>
    private static async Task<bool> SafeAsync(Func<Task<bool>> call)
    {
        try { return await call().ConfigureAwait(false); }
        catch (Exception) { return false; }
    }

    private static async Task SafeAsync(Func<Task> call)
    {
        try { await call().ConfigureAwait(false); }
        catch (Exception) { /* The backend logs its own failures. */ }
    }

    private void RaiseChanged() => Changed?.Invoke();
}
