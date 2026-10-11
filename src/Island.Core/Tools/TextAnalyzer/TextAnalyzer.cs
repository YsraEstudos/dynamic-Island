using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Island.Core.Tools.TextAnalyzer;

/// <summary>
/// Counts words, sentences, paragraphs and reading time of a text. Pure: the same text always gives the same numbers.
/// </summary>
public static class TextAnalyzer
{
    /// <summary>Silent reading speed for pt-BR text.</summary>
    public const int ReadingWordsPerMinute = 200;

    /// <summary>Speaking speed, shown as the tooltip of the reading time.</summary>
    public const int SpeakingWordsPerMinute = 130;

    // An apostrophe or hyphen belongs to a word only when letters or digits sit on both sides of it.
    private static readonly Regex WordPattern = new(
        @"[\p{L}\p{M}\p{N}]+(?:['’\-‐‑][\p{L}\p{M}\p{N}]+)*",
        RegexOptions.CultureInvariant);

    public static TextStats Analyze(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return TextStats.Empty;

        // The clipboard hands over Windows line endings; one newline is one line break.
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        ElementCounts elements = CountElements(normalized);
        int words = WordPattern.Count(normalized);
        (int paragraphs, int lines) = CountBlocks(normalized);

        return new TextStats(
            Characters: elements.Characters,
            CharactersNoSpaces: elements.CharactersNoSpaces,
            Words: words,
            Sentences: CountSentences(normalized),
            Paragraphs: paragraphs,
            Lines: lines,
            ReadingTime: TimeFor(words, ReadingWordsPerMinute),
            SpeakingTime: TimeFor(words, SpeakingWordsPerMinute),
            EstimatedTokens: EstimateTokens(elements.HalfUnits, words));
    }

    private readonly record struct ElementCounts(int Characters, int CharactersNoSpaces, long HalfUnits);

    private static ElementCounts CountElements(string text)
    {
        int characters = 0;
        int noSpaces = 0;
        long halfUnits = 0;

        // Text elements, not UTF-16 units: an emoji or a letter with a combining accent is one character.
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();
            characters++;
            if (!string.IsNullOrWhiteSpace(element)) noSpaces++;
            halfUnits += Ascii.IsValid(element) ? 2 : 3;
        }

        return new ElementCounts(characters, noSpaces, halfUnits);
    }

    /// <summary>A paragraph is a block of lines with text; blank lines (or lines of spaces only) separate blocks.</summary>
    private static (int Paragraphs, int Lines) CountBlocks(string text)
    {
        int paragraphs = 0;
        int lines = 0;
        bool inParagraph = false;

        foreach (string line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                inParagraph = false;
                continue;
            }

            lines++;
            if (inParagraph) continue;
            paragraphs++;
            inParagraph = true;
        }

        return (paragraphs, lines);
    }

    /// <summary>
    /// A run of . ! ? or … is one sentence end, however long the run. Text after the last end is one more sentence,
    /// so a line without punctuation still counts. Abbreviations are not special-cased.
    /// </summary>
    private static int CountSentences(string text)
    {
        int sentences = 0;
        bool inRun = false;
        bool letterAfterRun = false;

        foreach (char c in text)
        {
            if (IsSentenceEnd(c))
            {
                if (!inRun) sentences++;
                inRun = true;
                letterAfterRun = false;
                continue;
            }

            inRun = false;
            if (char.IsLetterOrDigit(c)) letterAfterRun = true;
        }

        return letterAfterRun ? sentences + 1 : sentences;
    }

    private static bool IsSentenceEnd(char c) => c is '.' or '!' or '?' or '…';

    /// <summary>Rounded up to the whole second, so any text with words takes at least one second.</summary>
    private static TimeSpan TimeFor(int words, int wordsPerMinute) =>
        words == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds((words * 60L + wordsPerMinute - 1) / wordsPerMinute);

    /// <summary>
    /// Rough estimate for pt-BR text: the larger of (length / 3.6) and (words * 1.35), where a non-ASCII text
    /// element counts as 1.5 characters because accents and emoji cost more tokens. The length is passed in halves
    /// (2 per ASCII element, 3 otherwise) to keep the arithmetic in integers.
    /// </summary>
    private static int EstimateTokens(long halfUnits, int words)
    {
        long byLength = (halfUnits * 5 + 35) / 36; // ceil(length / 3.6), since length = halfUnits / 2
        long byWords = (words * 27L + 19) / 20;    // ceil(words * 1.35)
        return (int)Math.Max(byLength, byWords);
    }
}
