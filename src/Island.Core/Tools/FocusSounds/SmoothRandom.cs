namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// A slow random curve in [0, 1]. It glides with smoothstep between random points a few seconds apart, which gives the
/// swells, gusts and café murmur their unhurried, organic movement without a fixed, audible period.
/// </summary>
internal sealed class SmoothRandom
{
    private readonly XorShift _rng;
    private readonly int _minLength;
    private readonly int _maxLength;
    private float _from;
    private float _to;
    private int _elapsed;
    private int _length;

    public SmoothRandom(XorShift rng, int sampleRate, double minSeconds, double maxSeconds)
    {
        _rng = rng;
        _minLength = Math.Max(1, (int)(minSeconds * sampleRate));
        _maxLength = Math.Max(_minLength, (int)(maxSeconds * sampleRate));
    }

    public float Next()
    {
        if (_elapsed >= _length)
        {
            _from = _to;
            _to = _rng.NextUnit();
            _length = _minLength + (int)((_maxLength - _minLength) * _rng.NextUnit());
            _elapsed = 0;
        }

        float t = (float)_elapsed / _length;
        float eased = t * t * (3f - 2f * t);
        _elapsed++;
        return _from + (_to - _from) * eased;
    }

    public void Reset()
    {
        _rng.Reset();
        _from = _to = 0f;
        _elapsed = 0;
        _length = 0;
    }
}
