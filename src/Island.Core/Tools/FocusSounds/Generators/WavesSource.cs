namespace Island.Core.Tools.FocusSounds.Generators;

/// <summary>
/// Waves: pink noise that swells about once every ten seconds. The swell opens and closes a low-pass, so the surf gets
/// brighter as it rises and duller as it falls back, like a real wave breaking.
/// </summary>
internal sealed class WavesSource : ISoundSource
{
    private const float Gain = 3.0f;
    private const int RetuneInterval = 64;

    private readonly int _sampleRate;
    private readonly XorShift _rng;
    private readonly PinkFilter _pink = new();
    private readonly OnePoleLowPass _tone;
    private readonly SmoothRandom _swell;
    private int _untilRetune;

    public WavesSource(int sampleRate, uint seed)
    {
        _sampleRate = sampleRate;
        _rng = new XorShift(seed);
        _tone = new OnePoleLowPass(sampleRate, 400.0);
        _swell = new SmoothRandom(new XorShift(XorShift.Derive(seed, 1)), sampleRate, minSeconds: 6.0, maxSeconds: 12.0);
    }

    public void Fill(Span<float> mono)
    {
        for (int i = 0; i < mono.Length; i++)
        {
            float swell = _swell.Next();
            if (_untilRetune-- <= 0)
            {
                _untilRetune = RetuneInterval;
                _tone.SetCutoff(180.0 + 1500.0 * swell * swell);
            }

            float body = _tone.Process(_pink.Process(_rng.NextBipolar()));
            mono[i] = SampleLimit.Clamp(body * (0.15f + 0.85f * swell) * Gain);
        }
    }

    public void Reset()
    {
        _rng.Reset();
        _pink.Reset();
        _tone.Reset();
        _swell.Reset();
        _untilRetune = 0;
    }
}
