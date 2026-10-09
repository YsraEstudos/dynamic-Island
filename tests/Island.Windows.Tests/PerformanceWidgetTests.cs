using Island.App.Composition;
using Island.App.Views.Widgets;
using Island.App.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Island.Windows.Tests;

public sealed class PerformanceWidgetTests
{
    [Fact]
    public void Catalog_contains_one_300_dip_performance_widget()
    {
        WidgetDescriptor widget = Assert.Single(WidgetCatalog.All, item => item.Id == "performance");

        Assert.Equal("Desempenho", widget.Title);
        Assert.Equal("Icon.Widget.Performance", widget.IconKey);
        Assert.Equal(300, widget.Width);
    }

    [Fact]
    public void Widget_loads_its_xaml_and_icon_resources_on_the_ui_thread()
    {
        WpfStaTestHost.Run(_ =>
        {
            // Demo services: the sampler is fake, so building the widget reads no sensor and starts no timer.
            using ServiceProvider services = ServiceRegistration.Build(demo: true, NullLoggerFactory.Instance);
            var context = services.GetRequiredService<Island.App.Widgets.ShelfContext>();

            var widget = new PerformanceWidget(context);

            Assert.Equal(300, widget.Width);
            Assert.Equal(WidgetCatalog.WidgetHeight, widget.Height);
        });
    }
}
