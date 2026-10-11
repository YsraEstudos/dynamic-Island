namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// Linear gain that moves toward a target over a set time, one sample at a time. Switching a sound on or off this way
/// avoids the click a sudden jump to silence would make.
/// </summary>
internal sealed class GainRamp
{
    private readonly int _sampleRate;
    private float _gain;
    private float _target;
    private float _step;

    public GainRamp(int sampleRate)
    {
        _sampleRate = sampleRate;
    }

    /// <summary>True when the output is and stays at zero, so the sources need not run.</summary>
    public bool IsSilent => _gain <= 0f && _target <= 0f;

    public void SetTarget(float target, double seconds)
    {
        _target = Math.Clamp(target, 0f, 1f);
        if (seconds <= 0.0)
        {
            _gain = _target;
            _step = 0f;
            return;
        }

        _step = (_target - _gain) / (float)(seconds * _sampleRate);
    }

    /// <summary>Gain for the next sample.</summary>
    public float Next()
    {
        if (_step == 0f) return _gain;

        _gain += _step;
        bool reached = _step > 0f ? _gain >= _target : _gain <= _target;
        if (reached)
        {
            _gain = _target;
            _step = 0f;
        }
        return _gain;
    }
}
