using Island.Core.Abstractions;

namespace Island.Core.Fakes;

/// <summary>In-memory <see cref="IDisplayService"/>. <see cref="SetFullscreen"/> raises <see cref="FullscreenChanged"/>.</summary>
public sealed class FakeDisplayService : IDisplayService
{
    private readonly object _gate = new();
    private bool _fullscreen;

    public event EventHandler<bool>? FullscreenChanged;

    public bool IsFullscreenAppActive
    {
        get
        {
            lock (_gate) return _fullscreen;
        }
    }

    public int StartCount { get; private set; }

    public void Start() => StartCount++;

    public void SetFullscreen(bool isFullscreen)
    {
        lock (_gate) _fullscreen = isFullscreen;
        FullscreenChanged?.Invoke(this, isFullscreen);
    }

    public void Dispose() { }
}
