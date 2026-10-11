using System.Text.Json;

namespace Island.Core.Tools.Counter;

/// <summary>
/// Saves the counter as <c>{ value, step, goal, label }</c> in <c>%LocalAppData%\DynamicIsland\counter.json</c>, or at
/// the path given. Saves are debounced: a burst of clicks becomes one write, <see cref="DebounceMilliseconds"/> after
/// the last change. <see cref="Flush"/> and disposing write a pending change at once. Reads and writes never throw:
/// a missing or broken file gives the default state, and a failed write is dropped.
/// </summary>
public sealed class CounterStore : IDisposable
{
    private const int DebounceMilliseconds = 400;
    private const string FileName = "counter.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private CounterState? _pending;
    private bool _disposed;

    public CounterStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", FileName);
        _timer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Reads the saved state. A missing, unreadable or invalid file gives <see cref="CounterState.Default"/>.</summary>
    public CounterState Load()
    {
        try
        {
            if (!File.Exists(_path)) return CounterState.Default;

            CounterFile? file = JsonSerializer.Deserialize<CounterFile>(File.ReadAllText(_path), JsonOptions);
            return file is null
                ? CounterState.Default
                : new CounterState(file.Value, file.Step, file.Goal, file.Label ?? CounterState.DefaultLabel);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return CounterState.Default;
        }
    }

    /// <summary>Queues <paramref name="state"/> for writing. Within a burst only the latest state is written.</summary>
    public void Save(CounterState state)
    {
        lock (_gate)
        {
            if (_disposed) return;

            _pending = state;
            _timer.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    /// <summary>Writes a queued state now instead of waiting for the debounce.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            WritePending();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            WritePending();
        }

        _timer.Dispose();
    }

    private void OnDebounceElapsed(object? _)
    {
        lock (_gate)
        {
            if (!_disposed) WritePending();
        }
    }

    private void WritePending()
    {
        if (_pending is not { } state) return;

        _pending = null;
        var file = new CounterFile { Value = state.Value, Step = state.Step, Goal = state.Goal, Label = state.Label };
        string temporary = _path + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (directory is not null) Directory.CreateDirectory(directory);

            File.WriteAllText(temporary, JsonSerializer.Serialize(file, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Dropped on purpose: the next change saves the whole state again.
        }
    }

    private sealed class CounterFile
    {
        public int Value { get; set; }
        public int Step { get; set; }
        public int Goal { get; set; }
        public string? Label { get; set; }
    }
}
