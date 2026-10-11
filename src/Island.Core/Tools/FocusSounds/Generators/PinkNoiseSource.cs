namespace Island.Core.Tools.FocusSounds.Generators;

/// <summary>Pink noise: equal energy per octave, so it sounds softer and fuller than white noise.</summary>
internal sealed class PinkNoiseSource : ISoundSource
{
    private const float Gain = 1.0f;

    private readonly XorShift _rng;
    private readonly PinkFilter _filter = new();

    public PinkNoiseSource(uint seed)
    {
        _rng = new XorShift(seed);
    }

    public void Fill(Span<float> mono)
    {
        for (int i = 0; i < mono.Length; i++)
        {
            mono[i] = SampleLimit.Clamp(_filter.Process(_rng.NextBipolar()) * Gain);
        }
    }

    public void Reset()
    {
        _rng.Reset();
        _filter.Reset();
    }
}
