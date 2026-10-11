using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Island.App.Composition;
using Island.App.Views.Widgets;
using Island.App.Widgets;
using Island.Core.Tools.FocusSounds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Island.Windows.Tests.Tools;

[Collection("Calendar WPF")]
public sealed class FocusSoundsWidgetTests
{
    [Fact]
    public void Widget_loads_its_xaml_and_icon_resources_on_the_ui_thread()
    {
        WpfStaTestHost.Run(_ =>
        {
            using ServiceProvider services = ServiceRegistration.Build(demo: true, NullLoggerFactory.Instance);
            var context = services.GetRequiredService<ShelfContext>();
            // A silent output: building the widget must never open a real audio device.
            context.Tools.GetOrCreate(() => new FocusSoundsModel(new NullFocusAudioOutput(), TimeProvider.System, seed: 1));

            var widget = new FocusSoundsWidget(context);

            Assert.Equal(340, widget.Width);
            Assert.Equal(WidgetCatalog.WidgetHeight, widget.Height);
            Assert.Equal(8, Assert.IsType<UniformGrid>(widget.FindName("ChipGrid")).Children.Count);
        });
    }

    [Fact]
    public void With_no_sound_on_it_asks_for_one()
    {
        WpfStaTestHost.Run(_ =>
        {
            using ServiceProvider services = ServiceRegistration.Build(demo: true, NullLoggerFactory.Instance);
            var context = services.GetRequiredService<ShelfContext>();
            context.Tools.GetOrCreate(() => new FocusSoundsModel(new NullFocusAudioOutput(), TimeProvider.System, seed: 1));
            var widget = new FocusSoundsWidget(context);

            Invoke(widget, "Refresh");

            TextBlock status = Assert.IsType<TextBlock>(widget.FindName("StatusText"));
            Assert.Equal(Visibility.Visible, status.Visibility);
            Assert.Equal(FocusSoundsFormat.PickASound, status.Text);
        });
    }

    [Fact]
    public void A_missing_audio_device_is_reported_on_the_widget()
    {
        WpfStaTestHost.Run(_ =>
        {
            using ServiceProvider services = ServiceRegistration.Build(demo: true, NullLoggerFactory.Instance);
            var context = services.GetRequiredService<ShelfContext>();
            var model = new FocusSoundsModel(new UnavailableOutput(), TimeProvider.System, seed: 1);
            context.Tools.GetOrCreate(() => model);
            model.SetOn(FocusSoundKind.Rain, true);
            model.Play();
            var widget = new FocusSoundsWidget(context);

            Invoke(widget, "Refresh");

            Assert.False(model.IsPlaying);
            TextBlock status = Assert.IsType<TextBlock>(widget.FindName("StatusText"));
            Assert.Equal(FocusSoundsFormat.NoAudioOutput, status.Text);
        });
    }

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);

    /// <summary>Stands in for a machine with no audio device: every start fails, as a real device refusal would.</summary>
    private sealed class UnavailableOutput : IFocusAudioOutput
    {
        public int SampleRate => 44100;

        public bool IsAvailable => false;

        public void Start(FocusMix mix)
        {
        }

        public void Stop()
        {
        }

        public void SetVolume(float volume)
        {
        }

        public void Dispose()
        {
        }
    }
}
