using Island.Core.Abstractions;
using Island.Core.Capture;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Capture;

/// <summary>
/// Screenshots (GDI copy, PNG written by <see cref="PngEncoder"/>) and recordings (GDI frames into an H.264 MP4) of the
/// monitor under the foreground window. Chosen over Windows.Graphics.Capture because it needs no Direct3D interop and
/// works on every Windows 10/11 machine; the trade-off is that exclusive full-screen games are not captured (see
/// <see cref="ScreenGrabber"/>). Work runs on the thread pool; the public methods never throw.
/// </summary>
public sealed class WindowsScreenCaptureService : IScreenCaptureService
{
    private const int Fps = 30;

    private readonly ILogger? _log;
    private readonly object _gate = new();
    private ScreenRecorder? _recorder;
    private bool _disposed;

    public WindowsScreenCaptureService(ILogger<WindowsScreenCaptureService>? log = null)
    {
        _log = log;
    }

    public event EventHandler? RecordingEnded;

    public bool IsRecording
    {
        get
        {
            lock (_gate) return _recorder is not null;
        }
    }

    public Task<bool> SaveScreenshotAsync(string path) => Task.Run(() => SaveScreenshot(path));

    public Task<bool> StartRecordingAsync(string path) => Task.Run(() => StartRecording(path));

    public Task StopRecordingAsync()
    {
        ScreenRecorder? recorder;
        lock (_gate)
        {
            recorder = _recorder;
            _recorder = null;
        }
        if (recorder is null) return Task.CompletedTask;

        // Dispose stops the thread and waits for the file to be finalized, so it runs off the caller's thread.
        return Task.Run(recorder.Dispose);
    }

    public void Dispose()
    {
        ScreenRecorder? recorder;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            recorder = _recorder;
            _recorder = null;
        }
        recorder?.Dispose();
    }

    private bool SaveScreenshot(string path)
    {
        try
        {
            if (!ScreenGrabber.TryGetForegroundMonitor(out ScreenRect monitor))
            {
                _log?.LogWarning("Screenshot skipped: no monitor for the foreground window.");
                return false;
            }

            using DibFrame frame = ScreenGrabber.Grab(monitor, monitor.Width, monitor.Height);
            byte[] png = PngEncoder.Encode(frame.Width, frame.Height, ReadPixels(frame));
            WriteFileAtomically(path, png);
            return true;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Screenshot failed.");
            return false;
        }
    }

    private bool StartRecording(string path)
    {
        lock (_gate)
        {
            if (_disposed || _recorder is not null) return false;

            try
            {
                if (!ScreenGrabber.TryGetForegroundMonitor(out ScreenRect monitor))
                {
                    _log?.LogWarning("Recording skipped: no monitor for the foreground window.");
                    return false;
                }

                (int width, int height) = CaptureSize.Fit(monitor.Width, monitor.Height);
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                var recorder = new ScreenRecorder(path, monitor, width, height, Fps, _log);
                recorder.EndedByError += () => OnRecorderEndedByError(recorder);
                recorder.Start();
                _recorder = recorder;
                return true;
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Recording could not start.");
                return false;
            }
        }
    }

    /// <summary>The recorder stopped itself (encoder or capture error). Clears the state and tells the controller once.</summary>
    private void OnRecorderEndedByError(ScreenRecorder recorder)
    {
        bool wasCurrent;
        lock (_gate)
        {
            wasCurrent = ReferenceEquals(_recorder, recorder);
            if (wasCurrent) _recorder = null;
        }

        if (wasCurrent) RecordingEnded?.Invoke(this, EventArgs.Empty);
        // Runs on the recording thread, which is already finishing. Dispose does not join the current thread.
        recorder.Dispose();
    }

    private static unsafe ReadOnlySpan<byte> ReadPixels(DibFrame frame) =>
        new((void*)frame.Pixels, frame.ByteCount);

    /// <summary>Writes to a temporary file and renames it, so a crash never leaves a half-written PNG in the folder.</summary>
    private static void WriteFileAtomically(string path, byte[] bytes)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: false);
    }
}
