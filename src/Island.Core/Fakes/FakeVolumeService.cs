using Island.Core.Abstractions;
using Island.Core.Models;

namespace Island.Core.Fakes;

/// <summary>In-memory <see cref="IVolumeService"/>. SetLevel and SetMuted raise <see cref="VolumeChanged"/> synchronously.</summary>
public sealed class FakeVolumeService : IVolumeService
{
    private readonly object _gate = new();
    private VolumeInfo _current = new(50, false);

    public event EventHandler<VolumeInfo>? VolumeChanged;

    public VolumeInfo Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public int InitializeCount { get; private set; }

    public void Initialize() => InitializeCount++;

    public void SetLevel(int level0to100)
    {
        VolumeInfo updated;
        lock (_gate)
        {
            updated = _current with { Level = Math.Clamp(level0to100, 0, 100) };
            _current = updated;
        }

        VolumeChanged?.Invoke(this, updated);
    }

    public void SetMuted(bool muted)
    {
        VolumeInfo updated;
        lock (_gate)
        {
            updated = _current with { IsMuted = muted };
            _current = updated;
        }

        VolumeChanged?.Invoke(this, updated);
    }

    public void Dispose() { }
}
