namespace Island.Core.Abstractions;

public interface IDisplayService : IDisposable
{
    /// <summary>Raised when a foreground fullscreen app appears/disappears. Arbitrary thread.</summary>
    event EventHandler<bool>? FullscreenChanged;

    bool IsFullscreenAppActive { get; }

    void Start();
}
