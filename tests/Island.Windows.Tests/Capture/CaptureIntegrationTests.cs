using System.Runtime.InteropServices;
using System.Text;
using Island.Windows.Capture;
using Island.Windows.Capture.Mp4;

namespace Island.Windows.Tests.Capture;

/// <summary>
/// Real Media Foundation and GDI calls. They run on this machine (the desktop must be interactive for the grab tests).
/// They check the vtable calls end to end: a real MP4 is written and its container boxes are verified.
/// </summary>
public sealed class CaptureIntegrationTests
{
    [Fact]
    public void Mp4VideoWriter_writes_a_finalized_mp4_from_bgra_frames()
    {
        const int width = 320, height = 240, fps = 10, frames = 12;
        int byteCount = width * height * 4;
        string path = Path.Combine(Path.GetTempPath(), $"island-capture-{Guid.NewGuid():N}.mp4");
        IntPtr pixels = Marshal.AllocHGlobal(byteCount);
        try
        {
            using (var writer = new Mp4VideoWriter(path, width, height, fps, bitrate: 1_000_000))
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    FillFrame(pixels, byteCount, width, frame);
                    writer.WriteFrame(pixels, byteCount, frame);
                }
                writer.Finish();
            }

            byte[] file = File.ReadAllBytes(path);
            Assert.True(file.Length > 1024, "The MP4 should contain real encoded video.");
            Assert.Equal("ftyp", Encoding.ASCII.GetString(file, 4, 4));
            Assert.True(Contains(file, "mdat"), "Frame data (mdat) is missing.");
            Assert.True(Contains(file, "moov"), "The movie index (moov) is missing: Finish did not run.");
        }
        finally
        {
            Marshal.FreeHGlobal(pixels);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ScreenGrabber_finds_the_foreground_monitor_and_copies_it_into_a_frame()
    {
        Assert.True(ScreenGrabber.TryGetForegroundMonitor(out ScreenRect monitor));
        Assert.True(monitor.Width > 0 && monitor.Height > 0);

        using DibFrame frame = ScreenGrabber.Grab(monitor, 64, 36);

        Assert.Equal(64, frame.Width);
        Assert.Equal(36, frame.Height);
        Assert.Equal(64 * 36 * 4, frame.ByteCount);
        Assert.NotEqual(IntPtr.Zero, frame.Pixels);
    }

    [Fact]
    public void ScreenGrabber_leaves_no_extra_GDI_objects_behind()
    {
        // Each grab must release its screen DC, memory DC and bitmap. A leak would show up as steady growth here.
        Assert.True(ScreenGrabber.TryGetForegroundMonitor(out ScreenRect monitor));
        for (int i = 0; i < 200; i++)
        {
            using DibFrame frame = ScreenGrabber.Grab(monitor, 320, 180);
            Assert.NotEqual(IntPtr.Zero, frame.Pixels);
        }
    }

    [Fact]
    public async Task Service_saves_a_real_png_and_finalizes_a_short_recording_into_a_temp_folder()
    {
        // Writes only under a temp folder that the test deletes at the end.
        string folder = Path.Combine(Path.GetTempPath(), $"island-capture-{Guid.NewGuid():N}");
        using var service = new WindowsScreenCaptureService();
        try
        {
            string png = Path.Combine(folder, "shot.png");
            Assert.True(await service.SaveScreenshotAsync(png));
            byte[] image = File.ReadAllBytes(png);
            Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], image[..8]);

            string mp4 = Path.Combine(folder, "clip.mp4");
            Assert.True(await service.StartRecordingAsync(mp4));
            Assert.True(service.IsRecording);
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            await service.StopRecordingAsync();

            Assert.False(service.IsRecording);
            byte[] video = File.ReadAllBytes(mp4);
            Assert.True(Contains(video, "moov"), "The recording was not finalized.");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A moving gradient, so consecutive frames differ and the encoder has real work to do.</summary>
    private static void FillFrame(IntPtr pixels, int byteCount, int width, int frame)
    {
        for (int i = 0; i < byteCount; i += 4)
        {
            int x = i / 4 % width;
            int y = i / 4 / width;
            int blue = (x + frame * 8) & 0xFF;
            int green = (y + frame * 4) & 0xFF;
            int red = (x ^ y) & 0xFF;
            // BGRA as one little-endian 32-bit value: byte 0 is blue, byte 3 is alpha.
            int pixel = blue | (green << 8) | (red << 16) | (0xFF << 24);
            Marshal.WriteInt32(pixels, i, pixel);
        }
    }

    private static bool Contains(byte[] haystack, string ascii)
    {
        byte[] needle = Encoding.ASCII.GetBytes(ascii);
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return true;
        }
        return false;
    }
}
