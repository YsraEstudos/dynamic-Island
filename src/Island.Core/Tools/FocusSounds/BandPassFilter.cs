namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// Second-order band-pass (RBJ cookbook, constant 0 dB peak). The centre can move while it runs; the coefficients are
/// recomputed in place, so changing it allocates nothing.
/// </summary>
internal sealed class BandPassFilter
{
    private readonly int _sampleRate;
    private readonly double _q;
    private float _b0;
    private float _a1;
    private float _a2;
    private float _x1;
    private float _x2;
    private float _y1;
    private float _y2;

    public BandPassFilter(int sampleRate, double q, double centreHz)
    {
        _sampleRate = sampleRate;
        _q = q;
        SetCentre(centreHz);
    }

    public void SetCentre(double centreHz)
    {
        double frequency = Math.Clamp(centreHz, 1.0, _sampleRate * 0.45);
        double omega = 2.0 * Math.PI * frequency / _sampleRate;
        double alpha = Math.Sin(omega) / (2.0 * _q);
        double a0 = 1.0 + alpha;
        _b0 = (float)(alpha / a0);
        _a1 = (float)(-2.0 * Math.Cos(omega) / a0);
        _a2 = (float)((1.0 - alpha) / a0);
    }

    public float Process(float input)
    {
        // b1 is zero and b2 is -b0 for this filter shape, so the feed-forward part is b0 * (x - x[n-2]).
        float output = _b0 * (input - _x2) - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1;
        _x1 = input;
        _y2 = _y1;
        _y1 = output;
        return output;
    }

    public void Reset()
    {
        _x1 = _x2 = _y1 = _y2 = 0f;
    }
}
