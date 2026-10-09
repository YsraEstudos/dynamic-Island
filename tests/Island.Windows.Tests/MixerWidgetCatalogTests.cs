using System.Windows.Media;
using Island.App.Widgets;

namespace Island.Windows.Tests;

public sealed class MixerWidgetCatalogTests
{
    [Fact]
    public void Catalog_contains_one_280_dip_mixer_widget()
    {
        WidgetDescriptor widget = Assert.Single(WidgetCatalog.All, item => item.Id == "mixer");

        Assert.Equal("Mixer", widget.Title);
        Assert.Equal("Icon.Widget.Mixer", widget.IconKey);
        Assert.Equal(280, widget.Width);
    }

    [Fact]
    public void Mixer_icon_resources_are_geometries()
    {
        WpfStaTestHost.Run(app =>
        {
            Assert.IsAssignableFrom<Geometry>(app.FindResource("Icon.Widget.Mixer"));
            Assert.IsAssignableFrom<Geometry>(app.FindResource("WidgetIcon.Speaker"));
            Assert.IsAssignableFrom<Geometry>(app.FindResource("WidgetIcon.SpeakerMuted"));
        });
    }
}
