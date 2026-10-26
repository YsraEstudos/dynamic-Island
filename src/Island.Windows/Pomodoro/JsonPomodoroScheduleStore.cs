using System.Text.Json;
using System.Text.Json.Serialization;
using Island.Core.Pomodoro;
using Island.Windows.Storage;

namespace Island.Windows.Pomodoro;

public sealed class JsonPomodoroScheduleStore(string path) : IPomodoroScheduleStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
    private bool _unavailable;

    public IReadOnlyList<ScheduledStart> Load()
    {
        _unavailable = false;
        try
        {
            var file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(path), Options);
            if (file?.Starts is null) throw new JsonException("Missing scheduled starts collection.");
            return file.Starts.Where(s => s is not null && Enum.TryParse<ScheduledStartMode>(s.Mode, out var mode)
                    && Enum.IsDefined(mode))
                .Select(s => new ScheduledStart(s.Id, s.At, Enum.Parse<ScheduledStartMode>(s.Mode!), s.Cycles,
                    s.FocusMinutes ?? PomodoroTimer.DefaultFocusMinutes, s.BreakMinutes ?? PomodoroTimer.DefaultBreakMinutes)).ToArray();
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        catch (JsonException)
        {
            try { AtomicJsonFile.Quarantine(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _unavailable = true; }
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _unavailable = true;
            return [];
        }
    }

    public void Save(IReadOnlyList<ScheduledStart> starts)
    {
        if (_unavailable) return;
        try
        {
            AtomicJsonFile.Write(path, new StoreFile { Starts = starts.Select(s => new StoreEntry
                { Id = s.Id, At = s.At, Mode = s.Mode.ToString(), Cycles = s.Cycles,
                  FocusMinutes = s.FocusMinutes, BreakMinutes = s.BreakMinutes }).ToList() }, Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError("Could not save scheduled starts: {0}", ex);
        }
    }

    private sealed class StoreFile { [JsonRequired] public List<StoreEntry>? Starts { get; set; } }
    private sealed class StoreEntry
    {
        public Guid Id { get; set; }
        public DateTimeOffset At { get; set; }
        public string? Mode { get; set; }
        public int Cycles { get; set; }
        // Null in files saved before the lengths were stored; the default focus and break apply then.
        public int? FocusMinutes { get; set; }
        public int? BreakMinutes { get; set; }
    }
}
