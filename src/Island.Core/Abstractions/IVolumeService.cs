using Island.Core.Models;

namespace Island.Core.Abstractions;

public interface IVolumeService : IDisposable
{
    /// <summary>Raised when master volume/mute changes (callback-driven, no polling). Raised from arbitrary threads.</summary>
    event EventHandler<VolumeInfo>? VolumeChanged;

    VolumeInfo Current { get; }

    void Initialize();
    void SetLevel(int level0to100);
    void SetMuted(bool muted);
}
