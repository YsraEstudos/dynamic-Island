using Island.App.Composition;
using Island.App.Views.Widgets;
using Island.App.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Island.Windows.Tests;

public sealed class TextAnalyzerWidgetTests
{
    [Fact]
    public void Widget_loads_its_xaml_and_icon_resources_on_the_ui_thread()
    {
        WpfStaTestHost.Run(_ =>
        {
            // Demo services: the clipboard is a fake, so building the widget touches no system clipboard.
            using ServiceProvider services = ServiceRegistration.Build(demo: true, NullLoggerFactory.Instance);
            var context = services.GetRequiredService<ShelfContext>();

            var widget = new TextAnalyzerWidget(context);

            Assert.Equal(320, widget.Width);
            Assert.Equal(WidgetCatalog.WidgetHeight, widget.Height);
        });
    }
}
