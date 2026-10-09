using Island.Core.GameNotes;

namespace Island.Core.Tests;

public sealed class GameNotesFormatTests
{
    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "1 nota")]
    [InlineData(3, "3 notas")]
    public void Count_text_uses_singular_and_plural(int count, string expected)
    {
        Assert.Equal(expected, GameNotesFormat.CountText(count));
    }

    [Fact]
    public void Empty_text_names_the_game_or_asks_for_one()
    {
        Assert.Equal("Abra um jogo para anotar nele", GameNotesFormat.EmptyText(null));
        Assert.Equal("Nenhuma nota para Celeste", GameNotesFormat.EmptyText("Celeste"));
    }

    [Theory]
    [InlineData(1, "+1 nota")]
    [InlineData(2, "+2 notas")]
    public void Overflow_text_counts_the_hidden_notes(int hidden, string expected)
    {
        Assert.Equal(expected, GameNotesFormat.OverflowText(hidden));
    }
}
