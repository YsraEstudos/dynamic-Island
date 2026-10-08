using System.Windows.Threading;

namespace Island.App.Widgets;

/// <summary>
/// Marshals a notification from any thread to the UI dispatcher and coalesces bursts: while one update is
/// pending, further <see cref="Signal"/> calls are absorbed. An optional delay lets a burst settle first.
/// </summary>
public sealed class UiSignal : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _action;
    private readonly TimeSpan _delay;
    private readonly DispatcherTimer? _timer;
    private int _pending;
    private volatile bool _disposed;

    /// <param name="dispatcher">UI dispatcher; the action runs on it.</param>
    /// <param name="action">Work to run on the UI thread.</param>
    /// <param name="delay">Optional coalescing window; zero runs as soon as the dispatcher is free.</param>
    public UiSignal(Dispatcher dispatcher, Action action, TimeSpan? delay = null)
    {
        _dispatcher = dispatcher;
        _action = action;
        _delay = delay ?? TimeSpan.Zero;

        if (_delay > TimeSpan.Zero)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = _delay };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                Run();
            };
        }
    }

    /// <summary>Requests an update. Safe to call from any thread.</summary>
    public void Signal()
    {
        if (_disposed) return;
        if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Fire));
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Stop();
    }

    private void Fire()
    {
        if (_disposed)
        {
            Interlocked.Exchange(ref _pending, 0);
            return;
        }

        if (_timer is null)
        {
            Run();
            return;
        }

        _timer.Stop();
        _timer.Start();
    }

    private void Run()
    {
        Interlocked.Exchange(ref _pending, 0);
        if (!_disposed) _action();
    }
}
