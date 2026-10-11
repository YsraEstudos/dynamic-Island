namespace Island.Core.Tools.FocusSounds.Generators;

/// <summary>
/// Rain: a soft hiss of white noise band-limited to the bright part of the spectrum, with sparse drop pulses on top.
/// The hiss is what makes it read as rain; the drops keep it from sounding like a fan.
/// </summary>
internal sealed class RainSource : ISoundSource
{
    private const float HissGain = 0.35f;
    private const float DropGain = 0.6f;

    private readonly XorShift _rng;
    private readonly OnePoleLowPass _hissLowCut;
    private readonly OnePoleLowPass _hissTone;
    private readonly ImpulseTrain _drops;
    private readonly XorShift _dropRng;

    public RainSource(int sampleRate, uint seed)
    {
        _rng = new XorShift(seed);
        _hissLowCut = new OnePoleLowPass(sampleRate, 700.0);
        _hissTone = new OnePoleLowPass(sampleRate, 9000.0);
        _dropRng = new XorShift(XorShift.Derive(seed, 1));
        _drops = new ImpulseTrain(new XorShift(XorShift.Derive(seed, 2)), sampleRate, perSecond: 30.0, decayMs: 4.0, 0.2f, 0.6f);
    }

    public void Fill(Span<float> mono)
    {
        for (int i = 0; i < mono.Length; i++)
        {
            float white = _rng.NextBipolar();
            float hiss = _hissTone.Process(white - _hissLowCut.Process(white));
            float drop = _drops.Next() * _dropRng.NextBipolar();
            mono[i] = SampleLimit.Clamp(hiss * HissGain + drop * DropGain);
        }
    }

    public void Reset()
    {
        _rng.Reset();
        _hissLowCut.Reset();
        _hissTone.Reset();
        _drops.Reset();
        _dropRng.Reset();
    }
}
