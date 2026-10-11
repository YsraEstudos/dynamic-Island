using Island.Core.Tools.FocusSounds;

namespace Island.Core.Tests.Tools.FocusSounds;

public class SoundSourceTests
{
    private const int SampleRate = 44100;
    private const int Frames = SampleRate * 2;

    public static IEnumerable<object[]> Kinds() =>
        Enum.GetValues<FocusSoundKind>().Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_sound_is_finite_within_range_and_not_silent(FocusSoundKind kind)
    {
        float[] samples = Generate(kind, seed: 7, Frames);

        Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
        Assert.All(samples, sample => Assert.InRange(sample, -0.96f, 0.96f));
        double rms = Rms(samples);
        Assert.InRange(rms, 0.01, 0.6);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Same_seed_gives_the_same_samples(FocusSoundKind kind)
    {
        float[] first = Generate(kind, seed: 42, 8192);
        float[] second = Generate(kind, seed: 42, 8192);

        Assert.Equal(first, second);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Different_seeds_give_different_samples(FocusSoundKind kind)
    {
        float[] first = Generate(kind, seed: 1, 8192);
        float[] second = Generate(kind, seed: 2, 8192);

        Assert.NotEqual(first, second);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Reset_restarts_the_sequence(FocusSoundKind kind)
    {
        ISoundSource source = SoundSourceFactory.Create(kind, SampleRate, seed: 9);
        float[] first = new float[8192];
        source.Fill(first);
        source.Fill(new float[8192]);

        source.Reset();
        float[] again = new float[8192];
        source.Fill(again);

        Assert.Equal(first, again);
    }

    [Fact]
    public void Brown_noise_is_much_smoother_than_white_noise()
    {
        double white = MeanStepSize(Generate(FocusSoundKind.WhiteNoise, seed: 3, Frames));
        double brown = MeanStepSize(Generate(FocusSoundKind.BrownNoise, seed: 3, Frames));

        Assert.True(brown < white * 0.2, $"brown step {brown:F4} should be far below white step {white:F4}");
    }

    [Fact]
    public void Pink_noise_sits_between_white_and_brown_in_smoothness()
    {
        double white = MeanStepSize(Generate(FocusSoundKind.WhiteNoise, seed: 5, Frames));
        double pink = MeanStepSize(Generate(FocusSoundKind.PinkNoise, seed: 5, Frames));
        double brown = MeanStepSize(Generate(FocusSoundKind.BrownNoise, seed: 5, Frames));

        Assert.True(white > pink, $"white {white:F4} should step more than pink {pink:F4}");
        Assert.True(pink > brown, $"pink {pink:F4} should step more than brown {brown:F4}");
    }

    [Fact]
    public void Factory_rejects_a_sample_rate_too_low_to_hear()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SoundSourceFactory.Create(FocusSoundKind.Rain, 1000, seed: 1));
    }

    private static float[] Generate(FocusSoundKind kind, uint seed, int frames)
    {
        ISoundSource source = SoundSourceFactory.Create(kind, SampleRate, seed);
        var samples = new float[frames];
        for (int start = 0; start < frames; start += 512)
        {
            source.Fill(samples.AsSpan(start, Math.Min(512, frames - start)));
        }
        return samples;
    }

    private static double Rms(float[] samples)
    {
        double sum = 0;
        foreach (float sample in samples) sum += sample * sample;
        return Math.Sqrt(sum / samples.Length);
    }

    /// <summary>Mean absolute change between neighbouring samples: small for low-frequency energy, large for hiss.</summary>
    private static double MeanStepSize(float[] samples)
    {
        double sum = 0;
        for (int i = 1; i < samples.Length; i++) sum += Math.Abs(samples[i] - samples[i - 1]);
        return sum / (samples.Length - 1);
    }
}
