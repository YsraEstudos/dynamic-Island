using Island.App.Composition;
using Island.App.Views.Widgets;
using Island.App.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Island.Windows.Tests.Tools;

public sealed class TimerWidgetTests
{
    [Fact]
    public void Widget_loads_its_xaml_and_resources_on_the_ui_thread()
    {
        WpfStaTestHost.Run(_ =>
        {
            using ServiceProvider services = ServiceRegistration.Build(demo: true, NullLoggerFactory.Instance);
            var context = services.GetRequiredService<ShelfContext>();

            var widget = new TimerWidget(context);

            Assert.Equal(300, widget.Width);
            Assert.Equal(WidgetCatalog.WidgetHeight, widget.Height);
        });
    }
}
