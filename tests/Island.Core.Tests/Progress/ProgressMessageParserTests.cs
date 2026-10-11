using Island.Core.Progress;

namespace Island.Core.Tests.Progress;

public class ProgressMessageParserTests
{
    private const string Valid =
        "{\"v\":1,\"id\":\"1234-7\",\"title\":\"Cortar: aula.mp4\",\"category\":\"Corte\",\"state\":\"running\"," +
        "\"percent\":42.5,\"message\":\"Cortando...\",\"source\":\"SuperPrograma\"}";

    /// <summary>A minimal valid line with optional extra members appended before the closing brace.</summary>
    private static string Line(string id = "1234-7", string title = "Cortar", string state = "running", string extra = "") =>
        "{\"v\":1,\"id\":\"" + id + "\",\"title\":\"" + title + "\",\"state\":\"" + state + "\"" + extra + "}";

    [Fact]
    public void Valid_line_is_parsed_with_all_fields()
    {
        Assert.True(ProgressMessageParser.TryParse(Valid, out ProgressUpdate? update));

        Assert.NotNull(update);
        Assert.Equal("1234-7", update.Id);
        Assert.Equal("Cortar: aula.mp4", update.Title);
        Assert.Equal("Corte", update.Category);
        Assert.Equal(ProgressState.Running, update.State);
        Assert.Equal(42.5, update.Percent);
        Assert.Equal("Cortando...", update.Message);
        Assert.Equal("SuperPrograma", update.Source);
    }

    [Fact]
    public void Optional_fields_absent_become_null()
    {
        Assert.True(ProgressMessageParser.TryParse(Line(), out ProgressUpdate? update));

        Assert.NotNull(update);
        Assert.Null(update.Category);
        Assert.Null(update.Percent);
        Assert.Null(update.Message);
        Assert.Null(update.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public void Malformed_or_non_object_lines_are_rejected(string line)
    {
        Assert.False(ProgressMessageParser.TryParse(line, out ProgressUpdate? update));
        Assert.Null(update);
    }

    [Theory]
    [InlineData("{\"v\":2,\"id\":\"a\",\"title\":\"t\",\"state\":\"running\"}")]
    [InlineData("{\"v\":\"1\",\"id\":\"a\",\"title\":\"t\",\"state\":\"running\"}")]
    [InlineData("{\"id\":\"a\",\"title\":\"t\",\"state\":\"running\"}")]
    public void Other_protocol_versions_are_rejected(string line)
    {
        Assert.False(ProgressMessageParser.TryParse(line, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_or_missing_id_is_rejected(string id)
    {
        Assert.False(ProgressMessageParser.TryParse(Line(id: id), out _));
    }

    [Fact]
    public void Id_of_64_chars_is_accepted_and_65_is_rejected()
    {
        string sixtyFour = new('x', 64);
        string sixtyFive = new('x', 65);

        Assert.True(ProgressMessageParser.TryParse(Line(id: sixtyFour), out ProgressUpdate? accepted));
        Assert.Equal(sixtyFour, accepted!.Id);
        Assert.False(ProgressMessageParser.TryParse(Line(id: sixtyFive), out _));
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("Running")]
    [InlineData("")]
    public void Unknown_state_is_rejected(string state)
    {
        Assert.False(ProgressMessageParser.TryParse(Line(state: state), out _));
    }

    [Fact]
    public void Missing_state_is_rejected()
    {
        Assert.False(ProgressMessageParser.TryParse("{\"v\":1,\"id\":\"a\",\"title\":\"t\"}", out _));
    }

    [Fact]
    public void Missing_or_blank_title_is_rejected()
    {
        Assert.False(ProgressMessageParser.TryParse("{\"v\":1,\"id\":\"a\",\"state\":\"running\"}", out _));
        Assert.False(ProgressMessageParser.TryParse(Line(title: "   "), out _));
    }

    [Fact]
    public void Title_is_trimmed_and_cut_to_80_chars()
    {
        Assert.True(ProgressMessageParser.TryParse(Line(title: "  Cortar  "), out ProgressUpdate? trimmed));
        Assert.Equal("Cortar", trimmed!.Title);

        Assert.True(ProgressMessageParser.TryParse(Line(title: new string('t', 100)), out ProgressUpdate? longTitle));
        Assert.Equal(80, longTitle!.Title.Length);
    }

    [Fact]
    public void Title_cut_never_splits_a_surrogate_pair()
    {
        // The emoji occupies chars 79 and 80; cutting at 80 would leave a lone high surrogate.
        string title = new string('a', 79) + "\U0001F600";

        Assert.True(ProgressMessageParser.TryParse(Line(title: title), out ProgressUpdate? update));

        Assert.Equal(79, update!.Title.Length);
        Assert.False(char.IsHighSurrogate(update.Title[^1]));
    }

    [Fact]
    public void Message_is_cut_to_120_chars()
    {
        Assert.True(ProgressMessageParser.TryParse(Line(extra: ",\"message\":\"" + new string('m', 300) + "\""), out ProgressUpdate? update));

        Assert.Equal(120, update!.Message!.Length);
    }

    [Fact]
    public void Blank_category_and_source_become_null_and_are_cut_to_40_chars()
    {
        Assert.True(ProgressMessageParser.TryParse(Line(extra: ",\"category\":\"  \",\"source\":\"" + new string('s', 60) + "\""), out ProgressUpdate? update));

        Assert.Null(update!.Category);
        Assert.Equal(40, update.Source!.Length);
    }

    [Theory]
    [InlineData(-5, 0.0)]
    [InlineData(150, 100.0)]
    [InlineData(0, 0.0)]
    [InlineData(100, 100.0)]
    public void Percent_is_clamped_to_0_100(double sent, double expected)
    {
        Assert.True(ProgressMessageParser.TryParse(Line(extra: $",\"percent\":{sent}"), out ProgressUpdate? update));

        Assert.Equal(expected, update!.Percent);
    }

    [Fact]
    public void Null_or_absent_percent_means_indeterminate()
    {
        Assert.True(ProgressMessageParser.TryParse(Line(extra: ",\"percent\":null"), out ProgressUpdate? update));
        Assert.Null(update!.Percent);
    }

    [Fact]
    public void Non_numeric_percent_means_indeterminate()
    {
        Assert.True(ProgressMessageParser.TryParse(Line(extra: ",\"percent\":\"50\""), out ProgressUpdate? update));
        Assert.Null(update!.Percent);
    }

    [Fact]
    public void Percent_beyond_double_range_means_indeterminate()
    {
        Assert.True(ProgressMessageParser.TryParse(Line(extra: ",\"percent\":1e400"), out ProgressUpdate? update));
        Assert.Null(update!.Percent);
    }

    [Fact]
    public void NaN_percent_is_not_valid_json_so_the_line_is_rejected()
    {
        Assert.False(ProgressMessageParser.TryParse(Line(extra: ",\"percent\":NaN"), out _));
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        Assert.True(ProgressMessageParser.TryParse(Line(extra: ",\"extra\":{\"x\":[1,2,3]},\"other\":true"), out ProgressUpdate? update));
        Assert.Equal("1234-7", update!.Id);
    }

    [Fact]
    public void Line_of_4096_chars_is_accepted_and_4097_is_rejected()
    {
        const string prefix = "{\"v\":1,\"id\":\"a\",\"title\":\"t\",\"state\":\"running\",\"extra\":\"";
        const string suffix = "\"}";
        string exact = prefix + new string('e', ProgressMessageParser.MaxLineChars - prefix.Length - suffix.Length) + suffix;
        string over = prefix + new string('e', ProgressMessageParser.MaxLineChars - prefix.Length - suffix.Length + 1) + suffix;

        Assert.Equal(ProgressMessageParser.MaxLineChars, exact.Length);
        Assert.True(ProgressMessageParser.TryParse(exact, out _));
        Assert.False(ProgressMessageParser.TryParse(over, out _));
    }
}
