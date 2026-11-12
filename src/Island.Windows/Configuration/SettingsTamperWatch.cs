namespace Island.Windows.Configuration;

/// <summary>
/// Watches <c>settings.json</c> while an Angry session is active and reports when another process edits it (PC-02: the
/// file is writable by the user and hand-editing it could weaken the phone block). The app answers by re-applying its
/// in-memory settings and warning the user. Detection is a light timestamp/length poll (no FileSystemWatcher handle to
/// leak); every save made by the app itself is recorded first through <see cref="NoteApplicationSave"/>.
/// </summary>
public sealed class SettingsTamperWatch : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly string _path;
    private readonly TimeSpan _pollInterval;
    private readonly object _gate = new();
    private Timer? _timer;
    private long _length = -1;
    private DateTime _lastWriteUtc = DateTime.MinValue;
    private bool _disposed;

    /// <param name="path">Full path of settings.json.</param>
    /// <param name="pollInterval">Polling period; defaults to two seconds (shortened in tests).</param>
    public SettingsTamperWatch(string path, TimeSpan? pollInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _pollInterval = pollInterval ?? PollInterval;
    }

    /// <summary>Raised on a thread-pool thread when the file changed outside the app. Handlers must not throw.</summary>
    public event Action? ChangedExternally;

    /// <summary>Starts polling. Idempotent; call it when an Angry session becomes active.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _timer is not null) return;
            Snapshot();
            _timer = new Timer(_ => Check(), state: null, _pollInterval, _pollInterval);
        }
    }

    /// <summary>Stops polling. Idempotent; called when the session ends.</summary>
    public void Stop()
    {
        Timer? timer;
        lock (_gate)
        {
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
    }

    /// <summary>Records the file as the app left it, so the app's own save is not reported as an external edit.</summary>
    public void NoteApplicationSave()
    {
        lock (_gate) Snapshot();
    }

    private void Check()
    {
        bool changed = false;
        lock (_gate)
        {
            if (_disposed || _timer is null) return;
            try
            {
                var file = new FileInfo(_path);
                if (file.Exists)
                {
                    if (file.Length != _length || file.LastWriteTimeUtc != _lastWriteUtc)
                    {
                        _length = file.Length;
                        _lastWriteUtc = file.LastWriteTimeUtc;
                        changed = true;
                    }
                }
                else if (_length != -1)
                {
                    _length = -1;
                    _lastWriteUtc = DateTime.MinValue;
                    changed = true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A transient read failure is not tampering; the next poll tries again.
            }
        }

        if (!changed) return;
        try
        {
            ChangedExternally?.Invoke();
        }
        catch (Exception)
        {
            // The watch must not die because a handler failed.
        }
    }

    private void Snapshot()
    {
        try
        {
            var file = new FileInfo(_path);
            if (file.Exists)
            {
                _length = file.Length;
                _lastWriteUtc = file.LastWriteTimeUtc;
            }
            else
            {
                _length = -1;
                _lastWriteUtc = DateTime.MinValue;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _length = -1;
            _lastWriteUtc = DateTime.MinValue;
        }
    }

    public void Dispose()
    {
        Timer? timer;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
    }
}
