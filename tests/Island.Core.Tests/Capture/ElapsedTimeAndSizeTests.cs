using Island.Core.Capture;

namespace Island.Core.Tests.Capture;

public sealed class ElapsedTimeAndSizeTests
{
    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(5, "00:05")]
    [InlineData(75, "01:15")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3725, "1:02:05")]
    public void ElapsedTime_formats_mm_ss_and_h_mm_ss(int seconds, string expected)
    {
        Assert.Equal(expected, ElapsedTime.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void ElapsedTime_shows_zero_for_a_negative_span()
    {
        Assert.Equal("00:00", ElapsedTime.Format(TimeSpan.FromSeconds(-3)));
    }

    [Fact]
    public void Fit_keeps_a_frame_that_already_fits()
    {
        Assert.Equal((1280, 720), CaptureSize.Fit(1280, 720));
    }

    [Fact]
    public void Fit_scales_a_4k_monitor_down_to_1080p()
    {
        Assert.Equal((1920, 1080), CaptureSize.Fit(3840, 2160));
    }

    [Theory]
    [InlineData(2560, 1441)]
    [InlineData(1366, 769)]
    [InlineData(1921, 1081)]
    public void Fit_always_returns_even_sizes_within_the_limit(int width, int height)
    {
        (int w, int h) = CaptureSize.Fit(width, height);

        Assert.True(w % 2 == 0 && h % 2 == 0, $"{w}x{h} must be even");
        Assert.True(w <= CaptureSize.DefaultMaxWidth && h <= CaptureSize.DefaultMaxHeight);
    }

    [Fact]
    public void Fit_rejects_an_empty_source()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CaptureSize.Fit(0, 1080));
    }

    [Fact]
    public void BitrateFor_is_clamped_between_2_and_12_Mbps()
    {
        Assert.Equal(2_000_000, CaptureSize.BitrateFor(320, 240, 30));
        Assert.Equal(12_000_000, CaptureSize.BitrateFor(3840, 2160, 60));
    }
}
