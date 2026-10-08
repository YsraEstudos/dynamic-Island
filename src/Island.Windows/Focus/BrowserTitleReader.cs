using Microsoft.Extensions.Logging;

namespace Island.Windows.Focus;

/// <summary>One accessibility read at a time, off the UI thread; newer requests replace pending ones.</summary>
internal sealed class BrowserTitleReader(Func<IntPtr, string?> read, Action<IntPtr, string?> deliver, ILogger? logger = null)
{
    private sealed record Request(IntPtr Window, SynchronizationContext? Context, long Version);
    private readonly object _gate = new();
    private Request? _pending;
    private long _version;
    private bool _reading;

    public void Queue(IntPtr hwnd, SynchronizationContext? context)
    {
        lock (_gate)
        {
            _pending = new Request(hwnd, context, ++_version);
            if (_reading) return;
            _reading = true;
        }
        _ = Task.Run(ReadPending);
    }

    public void Cancel()
    {
        lock (_gate)
        {
            ++_version;
            _pending = null;
        }
    }

    private void ReadPending()
    {
        while (true)
        {
            Request request;
            lock (_gate)
            {
                if (_pending is null)
                {
                    _reading = false;
                    return;
                }
                request = _pending;
                _pending = null;
            }

            string? title;
            try { title = read(request.Window); }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Browser accessibility title read failed.");
                title = null;
            }

            void Deliver()
            {
                lock (_gate)
                {
                    if (request.Version != _version) return;
                }
                // Do not hold the queue lock while calling back into the guard.
                deliver(request.Window, title);
            }

            try
            {
                if (request.Context is null) Deliver();
                else request.Context.Post(_ => Deliver(), null);
            }
            catch (Exception ex) { logger?.LogDebug(ex, "Browser title result could not be delivered."); }
        }
    }
}
