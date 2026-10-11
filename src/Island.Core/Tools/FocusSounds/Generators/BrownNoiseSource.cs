namespace Island.Core.Tools.FocusSounds.Generators;

/// <summary>Brown noise: deep and low, with most of its energy below a few hundred hertz.</summary>
internal sealed class BrownNoiseSource : ISoundSource
{
    private const float Gain = 1.0f;

    private readonly XorShift _rng;
    private readonly BrownWalk _walk = new();

    public BrownNoiseSource(uint seed)
    {
        _rng = new XorShift(seed);
    }

    public void Fill(Span<float> mono)
    {
        for (int i = 0; i < mono.Length; i++)
        {
            mono[i] = SampleLimit.Clamp(_walk.Process(_rng.NextBipolar()) * Gain);
        }
    }

    public void Reset()
    {
        _rng.Reset();
        _walk.Reset();
    }
}
