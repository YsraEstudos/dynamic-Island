namespace Island.Core.Tools.FocusSounds;

/// <summary>Where the focus mix goes: the speakers in the app, or a fake in tests. None of these calls may throw.</summary>
public interface IFocusAudioOutput : IDisposable
{
    /// <summary>Rate the mix must be generated at, which is the device's rate.</summary>
    int SampleRate { get; }

    /// <summary>False after the device failed to open. The widget then says there is no audio output.</summary>
    bool IsAvailable { get; }

    /// <summary>Opens the device and pulls the mix until <see cref="Stop"/>. Starting a running output does nothing.</summary>
    void Start(FocusMix mix);

    /// <summary>Releases the device. Safe to call when not started.</summary>
    void Stop();

    /// <summary>General volume, 0..1, applied on top of the mix.</summary>
    void SetVolume(float volume);
}
