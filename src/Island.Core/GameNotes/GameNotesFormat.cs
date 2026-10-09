namespace Island.Core.GameNotes;

/// <summary>Short Portuguese texts for the widget. Kept here so the wording can be tested without WPF.</summary>
public static class GameNotesFormat
{
    public static string CountText(int count) => count switch
    {
        <= 0 => string.Empty,
        1 => "1 nota",
        _ => $"{count} notas",
    };

    public static string EmptyText(string? gameName) => string.IsNullOrWhiteSpace(gameName)
        ? "Abra um jogo para anotar nele"
        : $"Nenhuma nota para {gameName}";

    public static string OverflowText(int hidden) => $"+{hidden} nota{(hidden == 1 ? string.Empty : "s")}";
}
