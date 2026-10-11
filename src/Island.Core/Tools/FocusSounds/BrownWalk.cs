namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// Brown (red) noise as a leaky integral of white noise. The small feedback keeps it from drifting away from zero, and
/// the energy falls off steeply with frequency, which is why it reads as rumble.
/// </summary>
internal sealed class BrownWalk
{
    private float _state;

    public float Process(float white)
    {
        _state = (_state + 0.02f * white) / 1.02f;
        return _state * 3.5f;
    }

    public void Reset() => _state = 0f;
}
