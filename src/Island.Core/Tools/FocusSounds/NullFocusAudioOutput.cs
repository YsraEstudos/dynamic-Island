namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// Output that opens nothing: the model and the widget work, but no sound leaves the process. Used by demo mode and
/// tests, so they never touch a real device.
/// </summary>
public sealed class NullFocusAudioOutput(int sampleRate = 44100) : IFocusAudioOutput
{
    public int SampleRate { get; } = sampleRate;

    public bool IsAvailable => true;

    public void Start(FocusMix mix)
    {
    }

    public void Stop()
    {
    }

    public void SetVolume(float volume)
    {
    }

    public void Dispose()
    {
    }
}
