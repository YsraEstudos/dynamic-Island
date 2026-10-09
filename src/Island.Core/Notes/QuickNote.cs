namespace Island.Core.Notes;

public enum QuickNoteColor
{
    Default,
    Blue,
    Green,
    Yellow,
    Pink,
    Purple
}

public enum QuickNoteCollection
{
    Active,
    Archived,
    Trash
}

public sealed record QuickNote(
    Guid Id,
    string Title,
    string Content,
    IReadOnlyList<QuickNoteChecklistItem> Checklist,
    IReadOnlyList<string> Tags,
    QuickNoteColor Color,
    bool IsPinned,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

public sealed record QuickNoteChecklistItem(Guid Id, string Text, bool IsCompleted, int Order);

public sealed record QuickNoteDraft(
    string Title,
    string Content,
    IReadOnlyList<QuickNoteChecklistItem> Checklist,
    IReadOnlyList<string> Tags,
    QuickNoteColor Color);
