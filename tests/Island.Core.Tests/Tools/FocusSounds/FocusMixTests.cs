using Island.Core.Tools.FocusSounds;

namespace Island.Core.Tests.Tools.FocusSounds;

public class FocusMixTests
{
    private const int SampleRate = 44100;

    [Fact]
    public void Nothing_is_heard_until_a_sound_is_on_and_the_mix_is_faded_in()
    {
        var mix = new FocusMix(SampleRate, seed: 1);
        mix.FadeTo(1f, 0);

        Assert.All(Pull(mix, 4410), sample => Assert.Equal(0f, sample));
        Assert.False(mix.AnySoundOn);
    }

    [Fact]
    public void A_sound_that_is_on_adds_signal()
    {
        var mix = new FocusMix(SampleRate, seed: 1);
        mix.FadeTo(1f, 0);
        mix.SetOn(FocusSoundKind.WhiteNoise, true);

        Assert.True(Rms(Pull(mix, 4410)) > 0.01);
    }

    [Fact]
    public void A_sound_at_zero_level_is_silent()
    {
        var mix = new FocusMix(SampleRate, seed: 1);
        mix.FadeTo(1f, 0);
        mix.SetOn(FocusSoundKind.Rain, true);
        mix.SetLevel(FocusSoundKind.Rain, 0f);

        Assert.All(Pull(mix, 4410), sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void Levels_are_clamped_to_zero_and_one()
    {
        var mix = new FocusMix(SampleRate, seed: 1);

        mix.SetLevel(FocusSoundKind.Fire, 2f);
        Assert.Equal(1f, mix.Level(FocusSoundKind.Fire));

        mix.SetLevel(FocusSoundKind.Fire, -1f);
        Assert.Equal(0f, mix.Level(FocusSoundKind.Fire));
    }

    [Fact]
    public void Every_sound_at_full_level_still_stays_inside_the_range()
    {
        var mix = new FocusMix(SampleRate, seed: 11);
        mix.FadeTo(1f, 0);
        foreach (FocusSoundKind kind in Enum.GetValues<FocusSoundKind>())
        {
            mix.SetOn(kind, true);
            mix.SetLevel(kind, 1f);
        }

        float[] samples = Pull(mix, SampleRate * 2);

        Assert.All(samples, sample => Assert.True(float.IsFinite(sample) && Math.Abs(sample) <= 1f));
    }

    [Fact]
    public void Turning_a_sound_off_removes_its_signal()
    {
        var mix = new FocusMix(SampleRate, seed: 1);
        mix.FadeTo(1f, 0);
        mix.SetOn(FocusSoundKind.WhiteNoise, true);
        mix.SetOn(FocusSoundKind.WhiteNoise, false);

        Assert.All(Pull(mix, 4410), sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void The_master_fade_ramps_in_over_the_requested_time()
    {
        var mix = new FocusMix(SampleRate, seed: 1);
        mix.SetOn(FocusSoundKind.WhiteNoise, true);
        mix.FadeTo(1f, seconds: 0.4);

        float[] block = Pull(mix, SampleRate / 2);
        double early = Rms(block[..441]);
        double late = Rms(block[^441..]);

        Assert.True(early < late * 0.1, $"start {early:F4} should be far quieter than the end {late:F4}");
    }

    [Fact]
    public void Fading_to_zero_ends_in_silence()
    {
        var mix = new FocusMix(SampleRate, seed: 1);
        mix.SetOn(FocusSoundKind.Waves, true);
        mix.FadeTo(1f, 0);
        Pull(mix, 4410);

        mix.FadeTo(0f, seconds: 0.1);
        float[] tail = Pull(mix, SampleRate / 2);

        Assert.All(tail[^4410..], sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void The_same_seed_gives_the_same_mix()
    {
        float[] first = PullFrom(seed: 5);
        float[] second = PullFrom(seed: 5);

        Assert.Equal(first, second);
    }

    private static float[] PullFrom(uint seed)
    {
        var mix = new FocusMix(SampleRate, seed);
        mix.SetOn(FocusSoundKind.Rain, true);
        mix.SetOn(FocusSoundKind.Cafe, true);
        mix.FadeTo(1f, 0);
        return Pull(mix, 8192);
    }

    private static float[] Pull(FocusMix mix, int frames)
    {
        var block = new float[frames];
        mix.MixInto(block);
        return block;
    }

    private static double Rms(float[] samples)
    {
        double sum = 0;
        foreach (float sample in samples) sum += sample * sample;
        return Math.Sqrt(sum / samples.Length);
    }
}
