namespace Island.Core.Tools.TextAnalyzer;

/// <summary>
/// Counts for one text. Characters are text elements, so an emoji or a letter with a combining accent counts as one.
/// Lines are the lines that hold visible text.
/// </summary>
public sealed record TextStats(
    int Characters,
    int CharactersNoSpaces,
    int Words,
    int Sentences,
    int Paragraphs,
    int Lines,
    TimeSpan ReadingTime,
    TimeSpan SpeakingTime,
    int EstimatedTokens)
{
    public static TextStats Empty { get; } = new(0, 0, 0, 0, 0, 0, TimeSpan.Zero, TimeSpan.Zero, 0);
}
