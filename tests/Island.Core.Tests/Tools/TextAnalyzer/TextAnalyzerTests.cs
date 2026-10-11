using Island.Core.Tools.TextAnalyzer;

namespace Island.Core.Tests.Tools;

public class TextAnalyzerTests
{
    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("palavra", count));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\t ")]
    public void Blank_text_has_no_statistics(string? text)
    {
        Assert.Equal(TextStats.Empty, TextAnalyzer.Analyze(text));
    }

    [Fact]
    public void Counts_a_short_portuguese_sentence()
    {
        TextStats stats = TextAnalyzer.Analyze("Olá, mundo!");

        Assert.Equal(11, stats.Characters);
        Assert.Equal(10, stats.CharactersNoSpaces);
        Assert.Equal(2, stats.Words);
        Assert.Equal(1, stats.Sentences);
        Assert.Equal(1, stats.Paragraphs);
        Assert.Equal(1, stats.Lines);
        Assert.Equal(TimeSpan.FromSeconds(1), stats.ReadingTime);
        Assert.Equal(TimeSpan.FromSeconds(1), stats.SpeakingTime);
        Assert.Equal(4, stats.EstimatedTokens);
    }

    [Theory]
    [InlineData("São Paulo é ótimo", 4)]
    [InlineData("guarda-chuva", 1)]
    [InlineData("d'água", 1)]
    [InlineData("d’água", 1)]
    [InlineData("pé-de-moleque", 1)]
    [InlineData("guarda-chuva d'água", 2)]
    [InlineData("fim - x", 2)]
    [InlineData("-x- 'y'", 2)]
    [InlineData("10-20 vezes", 2)]
    [InlineData("2024 vezes", 2)]
    [InlineData("é", 1)] // decomposed accent: the combining mark stays inside the word
    public void Words_are_letter_and_digit_runs_joined_by_inner_apostrophes_or_hyphens(string text, int expected)
    {
        Assert.Equal(expected, TextAnalyzer.Analyze(text).Words);
    }

    [Fact]
    public void Emoji_are_characters_but_not_words()
    {
        TextStats emoji = TextAnalyzer.Analyze("😀👍");
        Assert.Equal(2, emoji.Characters);
        Assert.Equal(0, emoji.Words);
        Assert.Equal(1, emoji.EstimatedTokens);

        TextStats mixed = TextAnalyzer.Analyze("ok 😀");
        Assert.Equal(4, mixed.Characters);
        Assert.Equal(3, mixed.CharactersNoSpaces);
        Assert.Equal(1, mixed.Words);
    }

    [Fact]
    public void Combining_accent_is_one_character()
    {
        Assert.Equal(1, TextAnalyzer.Analyze("é").Characters);
    }

    [Fact]
    public void Blank_lines_separate_paragraphs_and_lines_of_spaces_count_as_blank()
    {
        TextStats stats = TextAnalyzer.Analyze("Um.\n\nDois.\n   \nTrês.");

        Assert.Equal(3, stats.Paragraphs);
        Assert.Equal(3, stats.Lines);
        Assert.Equal(3, stats.Sentences);
    }

    [Fact]
    public void Windows_line_endings_count_like_plain_newlines()
    {
        Assert.Equal(TextAnalyzer.Analyze("a\nb"), TextAnalyzer.Analyze("a\r\nb"));
        Assert.Equal(3, TextAnalyzer.Analyze("a\r\nb").Characters);
        Assert.Equal(2, TextAnalyzer.Analyze("a\r\nb").Lines);
        Assert.Equal(2, TextAnalyzer.Analyze("a\r\n\r\nb").Paragraphs);
        Assert.Equal(2, TextAnalyzer.Analyze("a\rb").Lines);
    }

    [Fact]
    public void Tabs_and_newlines_are_not_counted_without_spaces()
    {
        TextStats stats = TextAnalyzer.Analyze("a\tb\nc");

        Assert.Equal(5, stats.Characters);
        Assert.Equal(3, stats.CharactersNoSpaces);
    }

    [Theory]
    [InlineData("Oi!!! Tudo bem?", 2)]
    [InlineData("Espere... e veja", 2)]
    [InlineData("Oi. Tudo", 2)]
    [InlineData("Ok?! Sim.", 2)]
    [InlineData("Olá", 1)]
    [InlineData("Fim.", 1)]
    [InlineData("…", 1)]
    [InlineData("...!!!", 1)]
    public void Each_run_of_sentence_ends_is_one_sentence_and_trailing_text_counts(string text, int expected)
    {
        Assert.Equal(expected, TextAnalyzer.Analyze(text).Sentences);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(130, 39, 60)]
    [InlineData(131, 40, 61)]
    [InlineData(200, 60, 93)]
    [InlineData(201, 61, 93)]
    public void Reading_and_speaking_times_round_up_to_the_second(int words, int readingSeconds, int speakingSeconds)
    {
        TextStats stats = TextAnalyzer.Analyze(Words(words));

        Assert.Equal(TimeSpan.FromSeconds(readingSeconds), stats.ReadingTime);
        Assert.Equal(TimeSpan.FromSeconds(speakingSeconds), stats.SpeakingTime);
    }

    [Fact]
    public void Text_without_words_takes_no_reading_time()
    {
        TextStats stats = TextAnalyzer.Analyze("... !!!");

        Assert.Equal(TimeSpan.Zero, stats.ReadingTime);
        Assert.Equal(TimeSpan.Zero, stats.SpeakingTime);
    }

    [Theory]
    [InlineData(36, 'a', 10)] // 36 / 3.6 exactly
    [InlineData(37, 'a', 11)] // just past the boundary rounds up
    [InlineData(36, 'ç', 15)] // non-ASCII counts 1.5: 54 / 3.6
    [InlineData(100, 'a', 135)] // word estimate wins: 100 words * 1.35
    public void Token_estimate_takes_the_larger_of_length_and_word_estimates(int count, char letter, int expected)
    {
        string text = count == 100
            ? string.Join(' ', Enumerable.Repeat(letter.ToString(), 100))
            : new string(letter, count);

        Assert.Equal(expected, TextAnalyzer.Analyze(text).EstimatedTokens);
    }

    [Fact]
    public void Token_estimate_follows_words_when_the_text_is_short_but_wordy()
    {
        // 10 words in 19 characters: length gives 6, words give 14.
        TextStats stats = TextAnalyzer.Analyze("a b c d e f g h i j");

        Assert.Equal(14, stats.EstimatedTokens);
    }
}
