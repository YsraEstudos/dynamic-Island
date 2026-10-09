using Island.App.Widgets;

namespace Island.Windows.Tests;

public sealed class GameNotesWidgetCatalogTests
{
    [Fact]
    public void Catalog_contains_one_280_dip_game_notes_widget()
    {
        WidgetDescriptor widget = Assert.Single(WidgetCatalog.All, item => item.Id == "gamenotes");

        Assert.Equal("Notas do Jogo", widget.Title);
        Assert.Equal("Icon.Widget.GameNotes", widget.IconKey);
        Assert.Equal(280, widget.Width);
        Assert.Equal(152, WidgetCatalog.WidgetHeight);
    }
}
