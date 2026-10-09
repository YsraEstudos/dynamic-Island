using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Island.Windows.Capture;

/// <summary>
/// A memory DC with a top-down 32-bit BGRA section. <see cref="CopyFromScreen"/> stretches a screen rectangle into it,
/// so one frame object serves both full-size screenshots and scaled recording frames.
/// Owns its GDI handles; dispose it to release them.
/// </summary>
internal sealed class DibFrame : IDisposable
{
    private IntPtr _dc;
    private IntPtr _bitmap;
    private IntPtr _previous;

    public DibFrame(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "The frame size must be positive.");
        Width = width;
        Height = height;

        _dc = CaptureNative.CreateCompatibleDC(IntPtr.Zero);
        if (_dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateCompatibleDC failed.");

        try
        {
            var info = new BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = width,
                biHeight = -height, // negative height = top-down rows, the same order as the recorder expects
                biPlanes = 1,
                biBitCount = 32,
                biCompression = CaptureNative.BI_RGB,
            };
            _bitmap = CaptureNative.CreateDIBSection(_dc, ref info, CaptureNative.DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            if (_bitmap == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateDIBSection failed.");
            Pixels = bits;
            _previous = CaptureNative.SelectObject(_dc, _bitmap);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Start of the BGRA pixels, valid until the frame is disposed.</summary>
    public IntPtr Pixels { get; }

    public int ByteCount => Width * Height * 4;

    /// <summary>Stretches <paramref name="source"/> (physical screen pixels) to fill this frame. Includes layered windows.</summary>
    public void CopyFromScreen(IntPtr screenDc, ScreenRect source)
    {
        // HALFTONE gives clean downscaling for recordings; it is a plain copy when the sizes match.
        CaptureNative.SetStretchBltMode(_dc, CaptureNative.HALFTONE);
        CaptureNative.SetBrushOrgEx(_dc, 0, 0, IntPtr.Zero);
        bool ok = CaptureNative.StretchBlt(_dc, 0, 0, Width, Height,
            screenDc, source.X, source.Y, source.Width, source.Height,
            CaptureNative.SRCCOPY | CaptureNative.CAPTUREBLT);
        if (!ok) throw new Win32Exception(Marshal.GetLastPInvokeError(), "StretchBlt failed.");
    }

    public void Dispose()
    {
        if (_dc != IntPtr.Zero)
        {
            if (_previous != IntPtr.Zero) CaptureNative.SelectObject(_dc, _previous);
            _previous = IntPtr.Zero;
        }
        if (_bitmap != IntPtr.Zero)
        {
            CaptureNative.DeleteObject(_bitmap);
            _bitmap = IntPtr.Zero;
        }
        if (_dc != IntPtr.Zero)
        {
            CaptureNative.DeleteDC(_dc);
            _dc = IntPtr.Zero;
        }
    }
}
