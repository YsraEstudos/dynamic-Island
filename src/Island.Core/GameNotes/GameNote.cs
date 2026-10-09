namespace Island.Core.GameNotes;

/// <summary>One checklist item written for a game. Completed and pinned flags only change the order and style.</summary>
public sealed record GameNote(
    Guid Id,
    string Text,
    DateTimeOffset CreatedAt,
    bool IsPinned,
    bool IsCompleted);
