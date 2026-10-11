namespace Island.Core.Tools.FocusSounds.Generators;

/// <summary>
/// Wind: white noise through a band-pass whose centre drifts slowly, with gusts that swell and fade. The moving centre
/// is what makes it sound like air rather than a static hiss.
/// </summary>
internal sealed class WindSource : ISoundSource
{
    private const float Gain = 2.0f;
    private const int RetuneInterval = 64;

    private readonly XorShift _rng;
    private readonly BandPassFilter _band;
    private readonly SmoothRandom _drift;
    private readonly SmoothRandom _gust;
    private int _untilRetune;

    public WindSource(int sampleRate, uint seed)
    {
        _rng = new XorShift(seed);
        _band = new BandPassFilter(sampleRate, q: 0.7, centreHz: 500.0);
        _drift = new SmoothRandom(new XorShift(XorShift.Derive(seed, 1)), sampleRate, minSeconds: 4.0, maxSeconds: 9.0);
        _gust = new SmoothRandom(new XorShift(XorShift.Derive(seed, 2)), sampleRate, minSeconds: 3.0, maxSeconds: 6.0);
    }

    public void Fill(Span<float> mono)
    {
        for (int i = 0; i < mono.Length; i++)
        {
            float drift = _drift.Next();
            float gust = _gust.Next();
            if (_untilRetune-- <= 0)
            {
                _untilRetune = RetuneInterval;
                _band.SetCentre(350.0 + 600.0 * drift);
            }

            float air = _band.Process(_rng.NextBipolar());
            mono[i] = SampleLimit.Clamp(air * (0.25f + 0.75f * gust) * Gain);
        }
    }

    public void Reset()
    {
        _rng.Reset();
        _band.Reset();
        _drift.Reset();
        _gust.Reset();
        _untilRetune = 0;
    }
}
