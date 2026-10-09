using Island.Core.Abstractions;
using Island.Core.GameNotes;

namespace Island.Core.Fakes;

/// <summary>In-memory <see cref="IForegroundGameTracker"/>. <see cref="Bring"/> simulates a game coming to the foreground.</summary>
public sealed class FakeForegroundGameTracker : IForegroundGameTracker
{
    private readonly object _gate = new();
    private GameInfo? _lastGame;

    public FakeForegroundGameTracker(GameInfo? initialGame = null)
    {
        _lastGame = initialGame;
    }

    public event Action<GameInfo>? CurrentGameChanged;

    public GameInfo? LastGame
    {
        get
        {
            lock (_gate) return _lastGame;
        }
    }

    public int StartCount { get; private set; }

    public void Start() => StartCount++;

    public void Bring(GameInfo game)
    {
        ArgumentNullException.ThrowIfNull(game);

        lock (_gate) _lastGame = game;
        CurrentGameChanged?.Invoke(game);
    }

    public void Dispose() { }
}
