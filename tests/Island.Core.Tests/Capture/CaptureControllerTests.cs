using Island.Core.Abstractions;
using Island.Core.Capture;
using Island.Core.Fakes;
using Island.Core.Models;

namespace Island.Core.Tests.Capture;

public sealed class CaptureControllerTests
{
    private const string Folder = @"C:\Users\test\Pictures\DynamicIsland\Capturas";

    [Fact]
    public async Task Screenshot_is_saved_in_the_folder_and_announced()
    {
        var fake = new FakeScreenCaptureService();
        var notices = new List<Notice>();
        var clock = new FakeClock();
        using var controller = new CaptureController(fake, Folder, notices.Add, clock: clock, fileExists: _ => false);
        string expectedName = CaptureNaming.ScreenshotFileName(clock.GetLocalNow().DateTime);

        bool saved = await controller.TakeScreenshotAsync();

        Assert.True(saved);
        Assert.Equal(1, fake.ScreenshotCount);
        Assert.Equal(Path.Combine(Folder, expectedName), controller.LastScreenshotPath);
        Notice notice = Assert.Single(notices);
        Assert.Equal("Print salvo", notice.Title);
        Assert.Equal(CaptureController.PrintGlyph, notice.Glyph);
    }

    [Fact]
    public async Task A_failed_screenshot_keeps_the_previous_one_and_says_so()
    {
        var fake = new FakeScreenCaptureService();
        var notices = new List<Notice>();
        string previous = Path.Combine(Folder, "Captura-2026-01-01_10-00-00.png");
        using var controller = new CaptureController(fake, Folder, notices.Add, latestScreenshotPath: previous, fileExists: _ => false);
        fake.FailScreenshots = true;

        bool saved = await controller.TakeScreenshotAsync();

        Assert.False(saved);
        Assert.Equal(previous, controller.LastScreenshotPath);
        Assert.Equal("Print não salvo", Assert.Single(notices).Title);
    }

    [Fact]
    public async Task A_name_already_in_use_gets_a_counter()
    {
        var fake = new FakeScreenCaptureService();
        var clock = new FakeClock();
        string first = Path.Combine(Folder, CaptureNaming.ScreenshotFileName(clock.GetLocalNow().DateTime));
        using var controller = new CaptureController(fake, Folder, _ => { }, clock: clock, fileExists: path => path == first);

        await controller.TakeScreenshotAsync();

        string expected = Path.Combine(Folder, Path.GetFileNameWithoutExtension(first) + " (2).png");
        Assert.Equal(expected, controller.LastScreenshotPath);
    }

    [Fact]
    public async Task Recording_starts_shows_elapsed_time_and_stops_with_a_duration_notice()
    {
        var fake = new FakeScreenCaptureService();
        var notices = new List<Notice>();
        var clock = new FakeClock();
        using var controller = new CaptureController(fake, Folder, notices.Add, clock: clock, fileExists: _ => false);

        Assert.True(await controller.StartRecordingAsync());
        Assert.Equal(RecordingPhase.Recording, controller.Phase);
        clock.Advance(TimeSpan.FromSeconds(75));
        Assert.Equal(TimeSpan.FromSeconds(75), controller.RecordingElapsed);

        Assert.True(await controller.StopRecordingAsync());

        Assert.Equal(RecordingPhase.Idle, controller.Phase);
        Assert.Equal(1, fake.RecordingStarts);
        Assert.Equal(1, fake.RecordingStops);
        Assert.Equal(["Gravação iniciada", "Gravação salva"], notices.Select(n => n.Title));
        Assert.Equal("01:15 em Capturas", notices[1].Subtitle);
    }

    [Fact]
    public async Task A_failed_start_returns_to_idle_and_says_so()
    {
        var fake = new FakeScreenCaptureService { FailStart = true };
        var notices = new List<Notice>();
        using var controller = new CaptureController(fake, Folder, notices.Add, fileExists: _ => false);

        Assert.False(await controller.StartRecordingAsync());

        Assert.Equal(RecordingPhase.Idle, controller.Phase);
        Assert.Equal("Gravação não iniciou", Assert.Single(notices).Title);
    }

    [Fact]
    public async Task Toggle_starts_when_idle_and_stops_when_recording()
    {
        var fake = new FakeScreenCaptureService();
        using var controller = new CaptureController(fake, Folder, _ => { }, fileExists: _ => false);

        await controller.ToggleRecordingAsync();
        Assert.Equal(RecordingPhase.Recording, controller.Phase);

        await controller.ToggleRecordingAsync();
        Assert.Equal(RecordingPhase.Idle, controller.Phase);
        Assert.Equal(1, fake.RecordingStops);
    }

    [Fact]
    public async Task A_recording_that_the_backend_ends_is_reported_once()
    {
        var fake = new FakeScreenCaptureService();
        var notices = new List<Notice>();
        using var controller = new CaptureController(fake, Folder, notices.Add, fileExists: _ => false);
        await controller.StartRecordingAsync();

        fake.EndRecordingUnexpectedly();
        fake.EndRecordingUnexpectedly();

        Assert.Equal(RecordingPhase.Idle, controller.Phase);
        Assert.Equal(["Gravação iniciada", "Gravação interrompida"], notices.Select(n => n.Title));
    }

    [Fact]
    public async Task Dispose_stops_a_running_recording_so_the_file_is_finalized()
    {
        var fake = new FakeScreenCaptureService();
        var controller = new CaptureController(fake, Folder, _ => { }, fileExists: _ => false);
        await controller.StartRecordingAsync();

        controller.Dispose();

        Assert.False(fake.IsRecording);
        Assert.Equal(1, fake.RecordingStops);
    }

    [Fact]
    public async Task Screenshot_during_a_recording_is_allowed()
    {
        var fake = new FakeScreenCaptureService();
        using var controller = new CaptureController(fake, Folder, _ => { }, fileExists: _ => false);
        await controller.StartRecordingAsync();

        Assert.True(await controller.TakeScreenshotAsync());
        Assert.Equal(RecordingPhase.Recording, controller.Phase);
    }

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
