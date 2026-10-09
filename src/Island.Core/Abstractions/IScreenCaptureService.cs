namespace Island.Core.Abstractions;

/// <summary>
/// Screen capture for the Capture widget. Implementations capture the monitor that holds the foreground window.
/// Methods report failure by returning false (they do not throw), so the island can show a notice instead.
/// </summary>
public interface IScreenCaptureService : IDisposable
{
    /// <summary>True while a recording is open.</summary>
    bool IsRecording { get; }

    /// <summary>Raised when a recording ends by itself (for example the encoder failed). Arbitrary thread.</summary>
    event EventHandler? RecordingEnded;

    /// <summary>Saves one PNG of the monitor under the foreground window to <paramref name="path"/>.</summary>
    Task<bool> SaveScreenshotAsync(string path);

    /// <summary>Opens an MP4 file at <paramref name="path"/> and starts recording the monitor under the foreground window.</summary>
    Task<bool> StartRecordingAsync(string path);

    /// <summary>Stops the open recording and finalizes the file. Does nothing when no recording is open.</summary>
    Task StopRecordingAsync();
}
