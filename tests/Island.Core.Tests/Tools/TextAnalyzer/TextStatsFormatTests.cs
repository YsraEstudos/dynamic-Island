using Island.Core.Tools.TextAnalyzer;

namespace Island.Core.Tests.Tools;

public class TextStatsFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1234, "1.234")]
    [InlineData(1234567, "1.234.567")]
    public void Numbers_use_a_pt_BR_thousands_separator(long value, string expected)
    {
        Assert.Equal(expected, TextStatsFormat.Number(value));
    }

    [Theory]
    [InlineData(0, "≈ 0")]
    [InlineData(42, "≈ 42")]
    [InlineData(9999, "≈ 9.999")]
    [InlineData(10000, "≈ 10 mil")]
    [InlineData(12345, "≈ 12,3 mil")]
    [InlineData(1234567, "≈ 1.235 mil")]
    public void Token_estimates_shorten_to_thousands_from_10000(int tokens, string expected)
    {
        Assert.Equal(expected, TextStatsFormat.Tokens(tokens));
    }

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(45, "45 s")]
    [InlineData(59, "59 s")]
    [InlineData(60, "1 min")]
    [InlineData(130, "2 min 10 s")]
    [InlineData(3600, "1 h")]
    [InlineData(3661, "1 h 01 min")]
    [InlineData(3900, "1 h 05 min")]
    public void Durations_read_naturally(int seconds, string expected)
    {
        Assert.Equal(expected, TextStatsFormat.Duration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Durations_round_to_the_nearest_second()
    {
        Assert.Equal("59 s", TextStatsFormat.Duration(TimeSpan.FromSeconds(59.4)));
        Assert.Equal("1 min", TextStatsFormat.Duration(TimeSpan.FromSeconds(59.5)));
    }

    [Fact]
    public void Preview_is_one_line_with_collapsed_spaces()
    {
        Assert.Equal("Oi tudo bem", TextStatsFormat.Preview("Oi\n\n  tudo\tbem"));
    }

    [Fact]
    public void Preview_of_blank_or_missing_text_is_empty()
    {
        Assert.Equal(string.Empty, TextStatsFormat.Preview(null));
        Assert.Equal(string.Empty, TextStatsFormat.Preview("   "));
    }

    [Fact]
    public void Preview_is_cut_to_48_characters_with_an_ellipsis()
    {
        string text = new('a', 60);

        Assert.Equal(new string('a', 48) + "…", TextStatsFormat.Preview(text));
    }

    [Fact]
    public void Preview_never_splits_an_emoji()
    {
        string text = new string('a', 47) + "😀bbb";

        Assert.Equal(new string('a', 47) + "…", TextStatsFormat.Preview(text));
    }

    [Fact]
    public void Preview_is_cut_when_the_text_is_longer_than_it_scans()
    {
        string text = "oi " + new string('x', 300);

        string preview = TextStatsFormat.Preview(text);

        Assert.EndsWith("…", preview);
        Assert.Equal(49, preview.Length);
    }
}
