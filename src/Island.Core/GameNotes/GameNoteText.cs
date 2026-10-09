namespace Island.Core.GameNotes;

/// <summary>Cleans user text before it becomes a note.</summary>
public static class GameNoteText
{
    /// <summary>Trims the text and caps its length. Returns null when nothing is left to save.</summary>
    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string trimmed = text.Trim();
        return trimmed.Length <= GameNotesBook.MaxTextLength ? trimmed : trimmed[..GameNotesBook.MaxTextLength];
    }
}
