using System.Windows;
using System.Windows.Threading;

namespace Island.Windows.Tests;

[CollectionDefinition("Calendar WPF", DisableParallelization = true)]
public sealed class CalendarWpfCollection { }

/// <summary>Runs WPF UI checks on one shared STA dispatcher without starting the production app.</summary>
internal static class WpfStaTestHost
{
    private static readonly Lazy<Host> SharedHost = new(() => new Host());

    public static void Run(Action<Application> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Host host = SharedHost.Value;
        host.Dispatcher.Invoke(
            () => action(host.Application),
            DispatcherPriority.Normal,
            CancellationToken.None,
            TimeSpan.FromSeconds(30));
    }

    // Far outside every monitor (Windows itself parks minimized windows here), so a test window never shows on the user's desktop.
    private const double OffscreenCoordinate = -32000;

    /// <summary>
    /// Parks a test window off-screen and stops it from taking focus. Call it before Show/ShowDialog.
    /// Layout, rendering to a bitmap and input events still work off-screen. A window with CenterOwner follows its owner.
    /// </summary>
    public static T KeepOffscreen<T>(T window) where T : Window
    {
        ArgumentNullException.ThrowIfNull(window);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = OffscreenCoordinate;
        window.Top = OffscreenCoordinate;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        return window;
    }

    private sealed class Host
    {
        private readonly ManualResetEventSlim _ready = new();
        private Exception? _startupFailure;

        public Host()
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    foreach (string resource in new[] { "Colors", "Typography", "Controls", "WidgetIcons" })
                    {
                        Application.Resources.MergedDictionaries.Add(new ResourceDictionary
                        {
                            Source = new Uri($"/DynamicIsland;component/Styles/{resource}.xaml", UriKind.Relative),
                        });
                    }
                    Dispatcher = Dispatcher.CurrentDispatcher;
                }
                catch (Exception ex) { _startupFailure = ex; }
                finally { _ready.Set(); }

                if (_startupFailure is null) Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "WPF test dispatcher",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("WPF test dispatcher did not start within 15 seconds.");
            if (_startupFailure is not null)
                throw new InvalidOperationException("WPF test dispatcher failed to initialize.", _startupFailure);
        }

        public Application Application { get; private set; } = null!;
        public Dispatcher Dispatcher { get; private set; } = null!;
    }
}
