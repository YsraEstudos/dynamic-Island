namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// First-order low-pass. Cheap enough to re-aim every few dozen samples, which is how the swells and the wind move
/// their tone without a per-sample exponential.
/// </summary>
internal sealed class OnePoleLowPass
{
    private readonly int _sampleRate;
    private float _alpha;
    private float _state;

    public OnePoleLowPass(int sampleRate, double cutoffHz)
    {
        _sampleRate = sampleRate;
        SetCutoff(cutoffHz);
    }

    public void SetCutoff(double cutoffHz)
    {
        double clamped = Math.Clamp(cutoffHz, 1.0, _sampleRate * 0.45);
        _alpha = (float)(1.0 - Math.Exp(-2.0 * Math.PI * clamped / _sampleRate));
    }

    public float Process(float input)
    {
        _state += _alpha * (input - _state);
        return _state;
    }

    public void Reset() => _state = 0f;
}
