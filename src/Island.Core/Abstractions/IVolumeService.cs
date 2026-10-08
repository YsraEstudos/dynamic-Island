using Island.Core.Models;

namespace Island.Core.Abstractions;

public interface IVolumeService : IDisposable
{
    /// <summary>Raised when master volume/mute changes (callback-driven, no polling). Raised from arbitrary threads.</summary>
    event EventHandler<VolumeInfo>? VolumeChanged;

    /// <summary>
    /// Raised when the value changed because the audio endpoint itself changed (default device switched, e.g. a Bluetooth
    /// headset moving to its hands-free profile when a microphone opens), not because anyone adjusted the volume.
    /// The new value is current, but it is not a user action, so it must not pop the volume indicator.
    /// </summary>
    event EventHandler<VolumeInfo>? DeviceVolumeChanged { add { } remove { } }

    VolumeInfo Current { get; }

    void Initialize();
    void SetLevel(int level0to100);
    void SetMuted(bool muted);
}
