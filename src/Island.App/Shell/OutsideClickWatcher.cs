using System.Windows.Threading;
using Island.Windows.Input;

namespace Island.App.Shell;

/// <summary>Dispatches physical mouse presses from the independent hook thread to the UI.</summary>
internal sealed class OutsideClickWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<int, int, bool> _isInside;
    private readonly Action _onOutsideClick;
    private readonly LowLevelMouseWatcher _watcher;
    private int _generation;
    private bool _active;

    public OutsideClickWatcher(Dispatcher dispatcher, Func<int, int, bool> isInside, Action onOutsideClick)
    {
        _dispatcher = dispatcher;
        _isInside = isInside;
        _onOutsideClick = onOutsideClick;
        _watcher = new LowLevelMouseWatcher((x, y) =>
        {
            int generation = Volatile.Read(ref _generation);
            if (!_dispatcher.HasShutdownStarted)
                _dispatcher.BeginInvoke(() =>
                {
                    if (_active && generation == _generation && !_isInside(x, y)) _onOutsideClick();
                });
        });
    }

    public void Start()
    {
        if (_active) return;
        _active = _watcher.Start();
    }

    public void Stop()
    {
        _active = false;
        Interlocked.Increment(ref _generation);
        _watcher.Stop();
    }

    public void Dispose() => Stop();
}
