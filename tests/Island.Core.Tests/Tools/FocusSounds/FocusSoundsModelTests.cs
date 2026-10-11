using Island.Core.Tools.FocusSounds;

namespace Island.Core.Tests.Tools.FocusSounds;

public class FocusSoundsModelTests
{
    private const int SampleRate = 44100;
    private static readonly DateTimeOffset Start = new(2026, 10, 10, 20, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider _time = new(Start);
    private readonly RecordingOutput _output = new();

    private FocusSoundsModel CreateModel() => new(_output, _time, seed: 1);

    [Fact]
    public void Play_does_nothing_while_no_sound_is_on()
    {
        using FocusSoundsModel model = CreateModel();

        model.Play();

        Assert.False(model.IsPlaying);
        Assert.Equal(0, _output.StartCount);
    }

    [Fact]
    public void Play_starts_the_output_and_fades_in()
    {
        using FocusSoundsModel model = CreateModel();
        model.SetOn(FocusSoundKind.WhiteNoise, true);

        model.Play();

        Assert.True(model.IsPlaying);
        Assert.True(_output.Running);
        float[] block = _output.Pull(SampleRate / 2);
        Assert.True(Rms(block[..441]) < Rms(block[^441..]) * 0.1);
    }

    [Fact]
    public void Pause_fades_out_and_stops_the_output_once_the_fade_is_over()
    {
        using FocusSoundsModel model = CreateModel();
        model.SetOn(FocusSoundKind.Rain, true);
        model.Play();

        model.Pause();
        Assert.False(model.IsPlaying);
        Assert.True(_output.Running);

        _time.Advance(FocusSoundsModel.FadeTime);
        _time.FireTimers();

        Assert.False(_output.Running);
        Assert.Equal(1, _output.StopCount);
    }

    [Fact]
    public void Pausing_again_before_the_fade_ends_keeps_the_output_running()
    {
        using FocusSoundsModel model = CreateModel();
        model.SetOn(FocusSoundKind.Rain, true);
        model.Play();
        model.Pause();

        _time.Advance(FocusSoundsModel.FadeTime / 2);
        _time.FireTimers();

        Assert.True(_output.Running);
    }

    [Fact]
    public void Turning_off_the_last_sound_pauses_playback()
    {
        using FocusSoundsModel model = CreateModel();
        model.SetOn(FocusSoundKind.Fire, true);
        model.Play();

        model.SetOn(FocusSoundKind.Fire, false);

        Assert.False(model.IsPlaying);
    }

    [Fact]
    public void A_device_that_fails_to_open_keeps_the_model_paused_and_reports_it()
    {
        _output.FailStart = true;
        using FocusSoundsModel model = CreateModel();
        model.SetOn(FocusSoundKind.Wind, true);

        model.Play();

        Assert.False(model.IsPlaying);
        Assert.False(model.IsAudioAvailable);
        Assert.False(_output.Running);
    }

    [Fact]
    public void The_general_volume_is_clamped_and_sent_to_the_output()
    {
        using FocusSoundsModel model = CreateModel();

        model.SetVolume(1.7f);
        Assert.Equal(1f, model.Volume);
        Assert.Equal(1f, _output.Volume);

        model.SetVolume(0.25f);
        Assert.Equal(0.25f, _output.Volume);
    }

    [Fact]
    public void Changes_are_announced_to_subscribers()
    {
        using FocusSoundsModel model = CreateModel();
        int announcements = 0;
        model.Changed += (_, _) => announcements++;

        model.SetOn(FocusSoundKind.Cafe, true);
        model.Play();

        Assert.Equal(2, announcements);
    }

    [Fact]
    public void Sleep_timer_stops_playback_at_the_deadline()
    {
        using FocusSoundsModel model = CreateModel();
        model.SetOn(FocusSoundKind.Rain, true);
        model.CycleSleepTimer();
        model.Play();

        _time.Advance(TimeSpan.FromMinutes(15));
        _time.FireTimers();
        Assert.False(model.IsPlaying);

        _time.Advance(FocusSoundsModel.FadeTime);
        _time.FireTimers();
        Assert.False(_output.Running);
    }

    [Fact]
    public void Sleep_timer_fades_the_last_ten_seconds_before_stopping()
    {
        using FocusSoundsModel model = CreateModel();
        model.SetOn(FocusSoundKind.WhiteNoise, true);
        model.CycleSleepTimer();
        model.Play();
        _output.Pull(SampleRate);

        _time.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(50));
        _time.FireTimers();
        float[] fade = _output.Pull(SampleRate * 10);

        Assert.True(Rms(fade[^4410..]) < Rms(fade[..4410]) * 0.1);
    }

    [Fact]
    public void The_sleep_countdown_shows_the_time_left_while_playing()
    {
        using FocusSoundsModel model = CreateModel();
        model.SetOn(FocusSoundKind.Waves, true);
        model.CycleSleepTimer();
        Assert.Null(model.SleepRemaining);

        model.Play();
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(10), model.SleepRemaining);
    }

    [Fact]
    public void Timers_exist_only_while_something_is_pending()
    {
        using FocusSoundsModel model = CreateModel();
        model.SetOn(FocusSoundKind.Rain, true);
        model.Play();
        Assert.Equal(0, _time.LiveTimerCount);

        model.Pause();
        Assert.Equal(1, _time.LiveTimerCount);

        _time.Advance(FocusSoundsModel.FadeTime);
        _time.FireTimers();
        Assert.Equal(0, _time.LiveTimerCount);
    }

    [Fact]
    public void Dispose_stops_and_disposes_the_output()
    {
        var model = CreateModel();
        model.SetOn(FocusSoundKind.Rain, true);
        model.Play();

        model.Dispose();

        Assert.False(_output.Running);
        Assert.Equal(1, _output.DisposeCount);
    }

    private static double Rms(float[] samples)
    {
        double sum = 0;
        foreach (float sample in samples) sum += sample * sample;
        return Math.Sqrt(sum / samples.Length);
    }
}
