using System.Text.Json;
using System.Text.Json.Serialization;
using Island.Core.Tools.Counter;
using Island.Windows.Storage;

namespace Island.Windows.Tools.Counter;

/// <summary>
/// Saves the counter as <c>{ value, step, goal, label }</c> in <c>%LocalAppData%\DynamicIsland\counter.json</c>, or at
/// the path given. Saves are debounced: a burst of clicks becomes one write, <see cref="DebounceMilliseconds"/> after
/// the last change. <see cref="Flush"/> and disposing write a pending change at once. Reads and writes never throw:
/// a missing or broken file gives the default state, and a failed write is dropped.
/// </summary>
public sealed class CounterStore : ICounterStore
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
    private bool _unavailable;

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
            _unavailable = false;

            CounterFile? file = JsonSerializer.Deserialize<CounterFile>(File.ReadAllText(_path), JsonOptions);
            if (file is null) throw new JsonException("Missing counter data.");
            return new CounterState(file.Value, file.Step, file.Goal, file.Label ?? CounterState.DefaultLabel);
        }
        catch (FileNotFoundException) { return CounterState.Default; }
        catch (DirectoryNotFoundException) { return CounterState.Default; }
        catch (JsonException)
        {
            try { AtomicJsonFile.Quarantine(_path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _unavailable = true; }
            return CounterState.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _unavailable = true;
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
        if (_unavailable || _pending is not { } state) return;

        var file = new CounterFile { Value = state.Value, Step = state.Step, Goal = state.Goal, Label = state.Label };
        try
        {
            AtomicJsonFile.Write(_path, file, JsonOptions);
            _pending = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Dropped on purpose: the next change saves the whole state again.
        }
    }

    private sealed class CounterFile
    {
        [JsonRequired]
        public int Value { get; set; }
        public int Step { get; set; }
        public int Goal { get; set; }
        public string? Label { get; set; }
    }
}
