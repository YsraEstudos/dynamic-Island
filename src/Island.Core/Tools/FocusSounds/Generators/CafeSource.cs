namespace Island.Core.Tools.FocusSounds.Generators;

/// <summary>
/// Café murmur: pink noise limited to the speech band, so it reads as distant voices rather than hiss. Slow random
/// swells on the level stand in for phrases and pauses, which keeps it from sounding like a constant tone.
/// </summary>
internal sealed class CafeSource : ISoundSource
{
    private const float Gain = 2.5f;

    private readonly XorShift _rng;
    private readonly PinkFilter _pink = new();
    private readonly OnePoleLowPass _speechLowCut;
    private readonly OnePoleLowPass _speechHighCut;
    private readonly SmoothRandom _babble;
    private readonly SmoothRandom _phrase;

    public CafeSource(int sampleRate, uint seed)
    {
        _rng = new XorShift(seed);
        _speechLowCut = new OnePoleLowPass(sampleRate, 300.0);
        _speechHighCut = new OnePoleLowPass(sampleRate, 3000.0);
        _babble = new SmoothRandom(new XorShift(XorShift.Derive(seed, 1)), sampleRate, minSeconds: 0.2, maxSeconds: 0.6);
        _phrase = new SmoothRandom(new XorShift(XorShift.Derive(seed, 2)), sampleRate, minSeconds: 2.0, maxSeconds: 5.0);
    }

    public void Fill(Span<float> mono)
    {
        for (int i = 0; i < mono.Length; i++)
        {
            float pink = _pink.Process(_rng.NextBipolar());
            float speech = _speechHighCut.Process(pink - _speechLowCut.Process(pink));
            float level = (0.2f + 0.8f * _babble.Next()) * (0.35f + 0.65f * _phrase.Next());
            mono[i] = SampleLimit.Clamp(speech * level * Gain);
        }
    }

    public void Reset()
    {
        _rng.Reset();
        _pink.Reset();
        _speechLowCut.Reset();
        _speechHighCut.Reset();
        _babble.Reset();
        _phrase.Reset();
    }
}
