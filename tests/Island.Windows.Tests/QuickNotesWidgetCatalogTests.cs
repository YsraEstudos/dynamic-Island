using Island.App.Widgets;

namespace Island.Windows.Tests;

public sealed class QuickNotesWidgetCatalogTests
{
    [Fact]
    public void Catalog_contains_one_280_dip_quick_notes_widget()
    {
        WidgetDescriptor widget = Assert.Single(WidgetCatalog.All, item => item.Id == "quicknotes");

        Assert.Equal("Notas", widget.Title);
        Assert.Equal("Icon.Widget.QuickNotes", widget.IconKey);
        Assert.Equal(280, widget.Width);
        Assert.Equal(152, WidgetCatalog.WidgetHeight);
    }
}
