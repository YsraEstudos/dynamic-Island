using Island.App.Widgets;

namespace Island.Windows.Tests.Capture;

public sealed class CaptureWidgetCatalogTests
{
    [Fact]
    public void Catalog_contains_one_280_dip_capture_widget()
    {
        WidgetDescriptor widget = Assert.Single(WidgetCatalog.All, item => item.Id == "capture");

        Assert.Equal("Captura", widget.Title);
        Assert.Equal("Icon.Widget.Capture", widget.IconKey);
        Assert.Equal(280, widget.Width);
    }
}
