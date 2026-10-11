using System.Windows.Media;
using Island.App.Widgets;

namespace Island.Windows.Tests;

public sealed class ToolWidgetCatalogTests
{
    [Theory]
    [InlineData("calculator", "Calculadora", "Icon.Widget.Calculator", 250)]
    [InlineData("counter", "Contador", "Icon.Widget.Counter", 240)]
    [InlineData("textanalyzer", "Analisador de Texto", "Icon.Widget.TextAnalyzer", 320)]
    [InlineData("timer", "Timer e Cronômetro", "Icon.Widget.Timer", 300)]
    [InlineData("focussounds", "Sons de Foco", "Icon.Widget.FocusSounds", 340)]
    public void Catalog_contains_the_tool_widget(string id, string title, string iconKey, double width)
    {
        WidgetDescriptor widget = Assert.Single(WidgetCatalog.All, item => item.Id == id);

        Assert.Equal(title, widget.Title);
        Assert.Equal(iconKey, widget.IconKey);
        Assert.Equal(width, widget.Width);
    }

    [Fact]
    public void Every_catalog_widget_has_a_unique_id_and_an_icon_geometry()
    {
        Assert.Equal(WidgetCatalog.All.Count, WidgetCatalog.All.Select(w => w.Id).Distinct().Count());

        WpfStaTestHost.Run(app =>
        {
            foreach (WidgetDescriptor widget in WidgetCatalog.All)
            {
                Assert.IsAssignableFrom<Geometry>(app.FindResource(widget.IconKey));
            }
        });
    }
}
