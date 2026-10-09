namespace Island.Core.Notes;

public interface IQuickNotesStore
{
    Task<IReadOnlyList<QuickNote>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<QuickNote> notes, CancellationToken cancellationToken = default);
}
