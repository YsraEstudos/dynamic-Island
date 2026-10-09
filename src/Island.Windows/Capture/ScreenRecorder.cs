using System.Diagnostics;
using Island.Core.Capture;
using Island.Windows.Capture.Mp4;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Capture;

/// <summary>
/// Records one monitor to an MP4 on a dedicated thread. Frames are taken on a wall-clock schedule: each frame is
/// stamped with the index of its time slot (time x fps), so a slow frame leaves a gap in the timeline instead of
/// stretching the video. The thread sleeps between slots, so an idle recording costs little CPU beyond the grab itself.
/// The file is finished on the recording thread before it exits, whether the stop was requested or an error occurred.
/// </summary>
internal sealed class ScreenRecorder : IDisposable
{
    private const int JoinTimeoutSeconds = 10;

    private readonly string _path;
    private readonly ScreenRect _source;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly ILogger? _log;
    private readonly ManualResetEventSlim _stop = new(false);
    private Mp4VideoWriter? _writer;
    private Thread? _thread;
    private volatile bool _stopRequested;
    private bool _disposed;

    public ScreenRecorder(string path, ScreenRect source, int width, int height, int fps, ILogger? log)
    {
        _path = path;
        _source = source;
        _width = width;
        _height = height;
        _fps = fps;
        _log = log;
    }

    /// <summary>Raised on the recording thread when the loop fails and the stop was not requested. Not raised on a normal stop.</summary>
    public event Action? EndedByError;

    /// <summary>Opens the MP4 and starts the thread. Throws when the file cannot be created.</summary>
    public void Start()
    {
        _writer = new Mp4VideoWriter(_path, _width, _height, _fps, CaptureSize.BitrateFor(_width, _height, _fps));
        try
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Island.ScreenRecorder" };
            _thread.Start();
        }
        catch
        {
            _writer.Dispose();
            _writer = null;
            throw;
        }
    }

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        long lastIndex = -1;
        bool failed = false;
        try
        {
            // One frame object for the whole recording: allocating a DIB per tick would cost ~8 MB per frame at 1080p.
            using var frame = new DibFrame(_width, _height);
            while (!_stop.IsSet)
            {
                long index = (long)(clock.Elapsed.TotalSeconds * _fps);
                if (index <= lastIndex)
                {
                    // Still inside the last slot: sleep until the next slot starts (or until a stop wakes us).
                    double wait = (lastIndex + 1) / (double)_fps - clock.Elapsed.TotalSeconds;
                    if (wait > 0) _stop.Wait(TimeSpan.FromSeconds(wait));
                    continue;
                }

                ScreenGrabber.CaptureInto(frame, _source);
                _writer!.WriteFrame(frame.Pixels, frame.ByteCount, index);
                lastIndex = index;
            }
        }
        catch (Exception ex)
        {
            failed = true;
            _log?.LogError(ex, "Screen recording failed while capturing or encoding a frame.");
        }
        finally
        {
            try
            {
                _writer?.Finish();
            }
            catch (Exception ex)
            {
                failed = true;
                _log?.LogError(ex, "Finishing the recording file failed; the MP4 may be unplayable.");
            }
            _writer?.Dispose();
            _writer = null;

            if (failed && !_stopRequested) EndedByError?.Invoke();
        }
    }

    /// <summary>Stops the thread, finalizes the file and releases the stop event. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopRequested = true;
        _stop.Set();

        Thread? thread = _thread;
        // Never join the current thread: EndedByError can dispose the recorder from inside its own loop.
        if (thread is not null && thread != Thread.CurrentThread && thread.IsAlive)
        {
            if (!thread.Join(TimeSpan.FromSeconds(JoinTimeoutSeconds)))
                _log?.LogWarning("The recording thread did not finish within {Seconds} s.", JoinTimeoutSeconds);
        }
        _stop.Dispose();
    }
}
