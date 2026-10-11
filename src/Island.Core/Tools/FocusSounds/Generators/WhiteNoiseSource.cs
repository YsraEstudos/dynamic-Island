namespace Island.Core.Tools.FocusSounds.Generators;

/// <summary>Uniform white noise: every sample is independent, so the energy is the same at every frequency.</summary>
internal sealed class WhiteNoiseSource : ISoundSource
{
    private const float Gain = 0.4f;

    private readonly XorShift _rng;

    public WhiteNoiseSource(uint seed)
    {
        _rng = new XorShift(seed);
    }

    public void Fill(Span<float> mono)
    {
        for (int i = 0; i < mono.Length; i++)
        {
            mono[i] = SampleLimit.Clamp(_rng.NextBipolar() * Gain);
        }
    }

    public void Reset() => _rng.Reset();
}
