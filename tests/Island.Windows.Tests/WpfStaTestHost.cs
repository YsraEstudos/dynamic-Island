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
