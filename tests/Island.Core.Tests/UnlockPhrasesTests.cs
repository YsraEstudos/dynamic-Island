using System.Globalization;
using System.Text;
using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public class UnlockPhrasesTests
{
    private const string Sample = "Eu estou escolhendo desistir do meu foco agora, mesmo sabendo que depois vou me arrepender de ter jogado fora esse tempo na internet.";

    [Fact]
    public void There_are_at_least_six_distinct_phrases()
    {
        Assert.True(UnlockPhrases.All.Count >= 6);
        Assert.Equal(UnlockPhrases.All.Count, UnlockPhrases.All.Distinct().Count());
    }

    [Fact]
    public void Every_phrase_is_between_120_and_200_characters()
    {
        foreach (var phrase in UnlockPhrases.All)
        {
            Assert.InRange(phrase.Length, 120, 200);
        }
    }

    [Fact]
    public void Phrases_have_no_edge_spaces_or_double_spaces()
    {
        foreach (var phrase in UnlockPhrases.All)
        {
            Assert.Equal(phrase.Trim(), phrase);
            Assert.DoesNotContain("  ", phrase);
        }
    }

    [Fact]
    public void Phrases_use_only_letters_spaces_and_ascii_punctuation()
    {
        foreach (var phrase in UnlockPhrases.All)
        {
            foreach (var c in phrase)
            {
                Assert.True(c == ' ' || char.IsLetter(c) || (c < 128 && char.IsPunctuation(c)), $"unexpected '{c}' in phrase");
                Assert.False(char.IsDigit(c));
            }
        }
    }

    [Fact]
    public void Pick_returns_one_of_the_phrases()
    {
        Assert.Contains(UnlockPhrases.Pick(), UnlockPhrases.All);
        Assert.Contains(UnlockPhrases.Pick(new Random(7)), UnlockPhrases.All);
    }

    [Fact]
    public void IsMatch_accepts_the_exact_phrase()
    {
        Assert.True(UnlockPhrases.IsMatch(Sample, Sample));
    }

    [Fact]
    public void IsMatch_is_case_sensitive()
    {
        Assert.False(UnlockPhrases.IsMatch(Sample, Sample.ToUpperInvariant()));
        Assert.False(UnlockPhrases.IsMatch("Eu desisto", "eu desisto"));
    }

    [Fact]
    public void IsMatch_ignores_accents_so_a_plain_keyboard_can_type_them()
    {
        Assert.True(UnlockPhrases.IsMatch("Não vou ceder, é só uma pausa.", "Nao vou ceder, e so uma pausa."));
        Assert.True(UnlockPhrases.IsMatch("Pedaço de tempo perdido", "Pedaco de tempo perdido"));
        Assert.True(UnlockPhrases.IsMatch(Sample, StripAccents(Sample)));
    }

    [Fact]
    public void IsMatch_rejects_one_wrong_character()
    {
        var chars = Sample.ToCharArray();
        chars[50] = chars[50] == 'z' ? 'y' : 'z';

        Assert.False(UnlockPhrases.IsMatch(Sample, new string(chars)));
    }

    [Fact]
    public void IsMatch_rejects_null()
    {
        Assert.False(UnlockPhrases.IsMatch(Sample, null));
    }

    [Fact]
    public void IsMatch_rejects_a_prefix_or_extra_whitespace()
    {
        Assert.False(UnlockPhrases.IsMatch(Sample, Sample[..60]));
        Assert.False(UnlockPhrases.IsMatch(Sample, Sample + " "));
        Assert.False(UnlockPhrases.IsMatch(Sample, " " + Sample));
    }

    [Fact]
    public void CommonPrefixLength_counts_matching_leading_characters()
    {
        Assert.Equal(3, UnlockPhrases.CommonPrefixLength("abcdef", "abcxyz"));
        Assert.Equal(3, UnlockPhrases.CommonPrefixLength("abc", "abc"));
        Assert.Equal(0, UnlockPhrases.CommonPrefixLength("abc", ""));
        Assert.Equal(0, UnlockPhrases.CommonPrefixLength("Abc", "abc"));
        Assert.Equal(Sample.Length, UnlockPhrases.CommonPrefixLength(Sample, Sample));
    }

    [Fact]
    public void CommonPrefixLength_is_zero_for_null_and_ignores_accents_like_IsMatch()
    {
        Assert.Equal(0, UnlockPhrases.CommonPrefixLength(Sample, null));
        Assert.Equal(4, UnlockPhrases.CommonPrefixLength("ação", "acao"));
    }

    [Fact]
    public void Keystroke_adding_one_character_is_plausible()
    {
        Assert.True(UnlockPhrases.IsPlausibleKeystroke("abc", "abcd"));
    }

    [Fact]
    public void Chunk_of_five_characters_in_one_step_is_not_plausible()
    {
        Assert.False(UnlockPhrases.IsPlausibleKeystroke("abc", "abcdefgh"));
    }

    [Fact]
    public void Deletions_and_same_length_replacements_are_plausible()
    {
        Assert.True(UnlockPhrases.IsPlausibleKeystroke("abcdef", "abc"));
        Assert.True(UnlockPhrases.IsPlausibleKeystroke("abc", "xyz"));
    }

    /// <summary>Test-side accent removal, written independently of the implementation under test.</summary>
    private static string StripAccents(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        }

        return builder.ToString();
    }
}
