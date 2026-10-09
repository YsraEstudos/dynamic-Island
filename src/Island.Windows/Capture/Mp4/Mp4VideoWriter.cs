using System.Runtime.InteropServices;

namespace Island.Windows.Capture.Mp4;

/// <summary>
/// Writes BGRA frames to an H.264 MP4 through the Media Foundation sink writer. The sink writer converts RGB32 to the
/// encoder's input format itself, as in Microsoft's "Using the Sink Writer to Encode Video" tutorial. Frames are stamped
/// by index, so the timeline follows the recording clock. Not thread-safe: one thread writes. Call
/// <see cref="Finish"/> before <see cref="Dispose"/>; without it the MP4 index is not written and the file cannot be played.
/// </summary>
internal sealed unsafe class Mp4VideoWriter : IDisposable
{
    private const long TicksPerSecond = 10_000_000;

    private readonly int _fps;
    private IntPtr _writer;
    private uint _stream;
    private bool _mediaStarted;
    private bool _finished;

    public Mp4VideoWriter(string path, int width, int height, int fps, int bitrate)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (width <= 0 || height <= 0 || (width & 1) != 0 || (height & 1) != 0)
            throw new ArgumentException("H.264 needs positive, even frame sizes.");
        if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));

        _fps = fps;
        Check(MediaFoundationNative.MFStartup(MediaFoundationNative.MfVersion, MediaFoundationNative.MfStartupFull));
        _mediaStarted = true;

        try
        {
            Check(MediaFoundationNative.MFCreateSinkWriterFromURL(path, IntPtr.Zero, IntPtr.Zero, out _writer));

            IntPtr outputType = CreateMediaType(MediaFoundationNative.VideoFormatH264, width, height, fps, bitrate);
            try
            {
                Check(MediaFoundationCalls.AddStream(_writer, outputType, out _stream));
            }
            finally
            {
                MediaFoundationCalls.Release(outputType);
            }

            IntPtr inputType = CreateMediaType(MediaFoundationNative.VideoFormatRgb32, width, height, fps, bitrate: 0);
            try
            {
                Check(MediaFoundationCalls.SetInputMediaType(_writer, _stream, inputType));
            }
            finally
            {
                MediaFoundationCalls.Release(inputType);
            }

            Check(MediaFoundationCalls.BeginWriting(_writer));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Writes one frame of <paramref name="byteCount"/> bytes of top-down BGRA pixels, stamped with <paramref name="frameIndex"/>.</summary>
    public void WriteFrame(IntPtr pixels, int byteCount, long frameIndex)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        if (byteCount <= 0) throw new ArgumentOutOfRangeException(nameof(byteCount));

        long duration = TicksPerSecond / _fps;
        long timestamp = frameIndex * TicksPerSecond / _fps;

        Check(MediaFoundationNative.MFCreateMemoryBuffer((uint)byteCount, out IntPtr buffer));
        try
        {
            Check(MediaFoundationCalls.Lock(buffer, out IntPtr data));
            try
            {
                Buffer.MemoryCopy((void*)pixels, (void*)data, byteCount, byteCount);
            }
            finally
            {
                MediaFoundationCalls.Unlock(buffer);
            }
            Check(MediaFoundationCalls.SetCurrentLength(buffer, (uint)byteCount));

            Check(MediaFoundationNative.MFCreateSample(out IntPtr sample));
            try
            {
                Check(MediaFoundationCalls.AddBuffer(sample, buffer));
                Check(MediaFoundationCalls.SetSampleTime(sample, timestamp));
                Check(MediaFoundationCalls.SetSampleDuration(sample, duration));
                Check(MediaFoundationCalls.WriteSample(_writer, _stream, sample));
            }
            finally
            {
                MediaFoundationCalls.Release(sample);
            }
        }
        finally
        {
            MediaFoundationCalls.Release(buffer);
        }
    }

    /// <summary>Completes the file. Required: it writes the MP4 index that players read.</summary>
    public void Finish()
    {
        if (_finished || _writer == IntPtr.Zero) return;
        _finished = true;
        Check(MediaFoundationCalls.FinalizeWriter(_writer));
    }

    public void Dispose()
    {
        if (_writer != IntPtr.Zero)
        {
            MediaFoundationCalls.Release(_writer);
            _writer = IntPtr.Zero;
        }
        if (_mediaStarted)
        {
            _mediaStarted = false;
            MediaFoundationNative.MFShutdown();
        }
    }

    private IntPtr CreateMediaType(Guid subtype, int width, int height, int fps, int bitrate)
    {
        Check(MediaFoundationNative.MFCreateMediaType(out IntPtr type));
        try
        {
            Check(MediaFoundationCalls.SetGuid(type, MediaFoundationNative.MfMtMajorType, MediaFoundationNative.MediaTypeVideo));
            Check(MediaFoundationCalls.SetGuid(type, MediaFoundationNative.MfMtSubtype, subtype));
            if (bitrate > 0)
                Check(MediaFoundationCalls.SetUInt32(type, MediaFoundationNative.MfMtAvgBitrate, (uint)bitrate));
            Check(MediaFoundationCalls.SetUInt32(type, MediaFoundationNative.MfMtInterlaceMode, MediaFoundationNative.MfPackedInterlaceProgressive));
            Check(MediaFoundationCalls.SetUInt64(type, MediaFoundationNative.MfMtFrameSize, MediaFoundationNative.Pack((uint)width, (uint)height)));
            Check(MediaFoundationCalls.SetUInt64(type, MediaFoundationNative.MfMtFrameRate, MediaFoundationNative.Pack((uint)fps, 1)));
            Check(MediaFoundationCalls.SetUInt64(type, MediaFoundationNative.MfMtPixelAspectRatio, MediaFoundationNative.Pack(1, 1)));
            return type;
        }
        catch
        {
            MediaFoundationCalls.Release(type);
            throw;
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }
}
