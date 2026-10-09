namespace Island.Core.GameNotes;

public interface IGameNotesStore
{
    Task<IReadOnlyList<GameNotesGame>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<GameNotesGame> games, CancellationToken cancellationToken = default);
}
