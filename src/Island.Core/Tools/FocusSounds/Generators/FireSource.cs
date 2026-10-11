namespace Island.Core.Tools.FocusSounds.Generators;

/// <summary>
/// Fireplace: a low brown-noise roar with short, sharp crackles at random moments. The crackles are very brief, so the
/// ear hears them as pops rather than as a steady tone.
/// </summary>
internal sealed class FireSource : ISoundSource
{
    private const float RumbleGain = 0.9f;
    private const float CrackleGain = 0.5f;

    private readonly XorShift _rng;
    private readonly BrownWalk _walk = new();
    private readonly OnePoleLowPass _rumble;
    private readonly ImpulseTrain _crackles;
    private readonly XorShift _crackleRng;

    public FireSource(int sampleRate, uint seed)
    {
        _rng = new XorShift(seed);
        _rumble = new OnePoleLowPass(sampleRate, 500.0);
        _crackleRng = new XorShift(XorShift.Derive(seed, 1));
        _crackles = new ImpulseTrain(new XorShift(XorShift.Derive(seed, 2)), sampleRate, perSecond: 5.0, decayMs: 1.5, 0.3f, 1.0f);
    }

    public void Fill(Span<float> mono)
    {
        for (int i = 0; i < mono.Length; i++)
        {
            float rumble = _rumble.Process(_walk.Process(_rng.NextBipolar()));
            float crackle = _crackles.Next() * _crackleRng.NextBipolar();
            mono[i] = SampleLimit.Clamp(rumble * RumbleGain + crackle * CrackleGain);
        }
    }

    public void Reset()
    {
        _rng.Reset();
        _walk.Reset();
        _rumble.Reset();
        _crackles.Reset();
        _crackleRng.Reset();
    }
}
