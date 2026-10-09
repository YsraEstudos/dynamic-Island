using Island.Core.Capture;

namespace Island.Core.Tests.Capture;

public sealed class CaptureNamingTests
{
    [Fact]
    public void Screenshot_and_recording_names_carry_the_local_time_stamp()
    {
        var time = new DateTime(2026, 10, 9, 14, 5, 7);

        Assert.Equal("Captura-2026-10-09_14-05-07.png", CaptureNaming.ScreenshotFileName(time));
        Assert.Equal("Gravacao-2026-10-09_14-05-07.mp4", CaptureNaming.RecordingFileName(time));
    }

    [Fact]
    public void Names_sort_by_time_as_text()
    {
        string earlier = CaptureNaming.ScreenshotFileName(new DateTime(2026, 1, 31, 23, 59, 59));
        string later = CaptureNaming.ScreenshotFileName(new DateTime(2026, 2, 1, 0, 0, 0));

        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    [Fact]
    public void MakeUnique_keeps_a_free_name()
    {
        string name = CaptureNaming.MakeUnique("Captura-x.png", _ => false);

        Assert.Equal("Captura-x.png", name);
    }

    [Fact]
    public void MakeUnique_appends_the_first_free_counter_before_the_extension()
    {
        var taken = new HashSet<string> { "Captura-x.png", "Captura-x (2).png" };

        string name = CaptureNaming.MakeUnique("Captura-x.png", taken.Contains);

        Assert.Equal("Captura-x (3).png", name);
    }

    [Theory]
    [InlineData("Captura-2026-10-09_12-00-00.png", true)]
    [InlineData("captura-2026.PNG", true)]
    [InlineData("Gravacao-2026-10-09_12-00-00.mp4", false)]
    [InlineData("Captura-2026.png.tmp", false)]
    [InlineData("notes.png", false)]
    public void IsScreenshotName_accepts_only_screenshot_files(string fileName, bool expected)
    {
        Assert.Equal(expected, CaptureNaming.IsScreenshotName(fileName));
    }

    [Fact]
    public void LatestScreenshot_picks_the_newest_write_time_among_screenshots_only()
    {
        var baseTime = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var files = new (string FileName, DateTime WrittenUtc)[]
        {
            ("Captura-2026-10-09_09-00-00.png", baseTime),
            ("Gravacao-2026-10-09_11-00-00.mp4", baseTime.AddHours(2)),
            ("Captura-2026-10-09_10-00-00.png", baseTime.AddHours(1)),
        };

        Assert.Equal("Captura-2026-10-09_10-00-00.png", CaptureNaming.LatestScreenshot(files));
    }

    [Fact]
    public void LatestScreenshot_is_null_without_screenshots()
    {
        Assert.Null(CaptureNaming.LatestScreenshot(Array.Empty<(string, DateTime)>()));
    }
}
