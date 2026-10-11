namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// Sparse random pulses that decay exponentially: the drops on a roof, the crackles in a fire. Each pulse starts at a
/// random amplitude; the caller multiplies the envelope by noise to give it a texture.
/// </summary>
internal sealed class ImpulseTrain
{
    private readonly XorShift _rng;
    private readonly float _chancePerSample;
    private readonly float _minAmplitude;
    private readonly float _maxAmplitude;
    private readonly float _decay;
    private float _envelope;

    public ImpulseTrain(XorShift rng, int sampleRate, double perSecond, double decayMs, float minAmplitude, float maxAmplitude)
    {
        _rng = rng;
        _chancePerSample = (float)(perSecond / sampleRate);
        _minAmplitude = minAmplitude;
        _maxAmplitude = maxAmplitude;
        _decay = (float)Math.Exp(-1000.0 / (decayMs * sampleRate));
    }

    /// <summary>Next envelope value, never negative.</summary>
    public float Next()
    {
        if (_rng.NextUnit() < _chancePerSample)
        {
            _envelope += _minAmplitude + (_maxAmplitude - _minAmplitude) * _rng.NextUnit();
        }

        float value = _envelope;
        _envelope *= _decay;
        return value;
    }

    public void Reset()
    {
        _rng.Reset();
        _envelope = 0f;
    }
}
