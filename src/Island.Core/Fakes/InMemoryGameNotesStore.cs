using Island.Core.GameNotes;

namespace Island.Core.Fakes;

/// <summary>
/// In-memory <see cref="IGameNotesStore"/> for --demo: never touches the real notes file. Starts with the given games.
/// </summary>
public sealed class InMemoryGameNotesStore : IGameNotesStore
{
    private readonly object _gate = new();
    private IReadOnlyList<GameNotesGame> _games;

    public InMemoryGameNotesStore(IEnumerable<GameNotesGame>? seed = null)
    {
        _games = seed?.ToArray() ?? Array.Empty<GameNotesGame>();
    }

    public Task<IReadOnlyList<GameNotesGame>> LoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_games);
    }

    public Task SaveAsync(IReadOnlyList<GameNotesGame> games, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(games);

        lock (_gate) _games = games.ToArray();
        return Task.CompletedTask;
    }
}
