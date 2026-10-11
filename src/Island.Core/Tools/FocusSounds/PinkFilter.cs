namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// Paul Kellet's economy pink-noise filter: seven one-pole stages driven by white noise, summed. It keeps the
/// -3 dB per octave slope closely enough that no listener can tell it from true pink noise.
/// </summary>
internal sealed class PinkFilter
{
    private float _b0;
    private float _b1;
    private float _b2;
    private float _b3;
    private float _b4;
    private float _b5;
    private float _b6;

    public float Process(float white)
    {
        _b0 = 0.99886f * _b0 + white * 0.0555179f;
        _b1 = 0.99332f * _b1 + white * 0.0750759f;
        _b2 = 0.96900f * _b2 + white * 0.1538520f;
        _b3 = 0.86650f * _b3 + white * 0.3104856f;
        _b4 = 0.55000f * _b4 + white * 0.5329522f;
        _b5 = -0.7616f * _b5 - white * 0.0168980f;
        float pink = _b0 + _b1 + _b2 + _b3 + _b4 + _b5 + _b6 + white * 0.5362f;
        _b6 = white * 0.115926f;
        return pink * 0.11f;
    }

    public void Reset()
    {
        _b0 = _b1 = _b2 = _b3 = _b4 = _b5 = _b6 = 0f;
    }
}
