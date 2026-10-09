using Island.Core.Abstractions;

namespace Island.Core.Fakes;

/// <summary>
/// In-memory <see cref="IScreenCaptureService"/> for demo mode and tests. It captures nothing and writes no files;
/// it only counts calls and can be told to fail.
/// </summary>
public sealed class FakeScreenCaptureService : IScreenCaptureService
{
    private readonly object _gate = new();
    private bool _recording;

    public event EventHandler? RecordingEnded;

    public bool FailScreenshots { get; set; }

    public bool FailStart { get; set; }

    public int ScreenshotCount { get; private set; }

    public int RecordingStarts { get; private set; }

    public int RecordingStops { get; private set; }

    public bool IsRecording
    {
        get
        {
            lock (_gate) return _recording;
        }
    }

    public Task<bool> SaveScreenshotAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        lock (_gate) ScreenshotCount++;
        return Task.FromResult(!FailScreenshots);
    }

    public Task<bool> StartRecordingAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        lock (_gate)
        {
            if (FailStart || _recording) return Task.FromResult(false);
            _recording = true;
            RecordingStarts++;
            return Task.FromResult(true);
        }
    }

    public Task StopRecordingAsync()
    {
        lock (_gate)
        {
            if (!_recording) return Task.CompletedTask;
            _recording = false;
            RecordingStops++;
        }
        return Task.CompletedTask;
    }

    /// <summary>Simulates the backend ending the recording by itself (for example an encoder failure).</summary>
    public void EndRecordingUnexpectedly()
    {
        lock (_gate)
        {
            if (!_recording) return;
            _recording = false;
        }
        RecordingEnded?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() { }
}
