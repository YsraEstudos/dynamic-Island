using System.Globalization;
using System.Text.Json;
using Island.Core.Pomodoro;
using Island.Windows.Storage;

namespace Island.Windows.Pomodoro;

public sealed class JsonPomodoroWeeklyScheduleStore(string path) : IPomodoroWeeklyScheduleStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
    private bool _unavailable;

    public PomodoroWeeklyScheduleState Load()
    {
        _unavailable = false;
        try
        {
            StoreFile? file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(path), Options);
            if (file?.Blocks is null) throw new JsonException("Missing weekly Pomodoro blocks collection.");

            var blocks = new List<PomodoroWeeklyBlock>();
            foreach (StoreBlock? entry in file.Blocks)
            {
                if (entry is null
                    || !Enum.TryParse(entry.Day, ignoreCase: true, out DayOfWeek day) || !Enum.IsDefined(day)
                    || !Enum.TryParse(entry.Mode, ignoreCase: true, out ScheduledStartMode mode) || !Enum.IsDefined(mode)
                    || !TimeOnly.TryParseExact(entry.Start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly start)
                    || !TimeOnly.TryParseExact(entry.End, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly end))
                    continue;
                blocks.Add(new PomodoroWeeklyBlock(entry.Id, day, start, end, mode, entry.Cycles, entry.Name ?? string.Empty));
            }

            return new PomodoroWeeklyScheduleState(blocks, file.Enabled, file.SkippedDate, file.ResumePromptPending);
        }
        catch (FileNotFoundException)
        {
            return new PomodoroWeeklyScheduleState([], Enabled: true, SkippedDate: null, ResumePromptPending: false);
        }
        catch (DirectoryNotFoundException)
        {
            return new PomodoroWeeklyScheduleState([], Enabled: true, SkippedDate: null, ResumePromptPending: false);
        }
        catch (JsonException)
        {
            try { AtomicJsonFile.Quarantine(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _unavailable = true; }
            return new PomodoroWeeklyScheduleState([], Enabled: true, SkippedDate: null, ResumePromptPending: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _unavailable = true;
            return new PomodoroWeeklyScheduleState([], Enabled: true, SkippedDate: null, ResumePromptPending: false);
        }
    }

    public void Save(PomodoroWeeklyScheduleState state)
    {
        if (_unavailable) return;
        try
        {
            AtomicJsonFile.Write(path, new StoreFile
            {
                Enabled = state.Enabled,
                SkippedDate = state.SkippedDate,
                ResumePromptPending = state.ResumePromptPending,
                Blocks = state.Blocks.Select(block => new StoreBlock
                {
                    Id = block.Id,
                    Day = block.Day.ToString(),
                    Start = block.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                    End = block.End.ToString("HH:mm", CultureInfo.InvariantCulture),
                    Mode = block.Mode.ToString(),
                    Cycles = block.Cycles,
                    Name = block.Name,
                }).ToList(),
            }, Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError("Could not save weekly Pomodoro schedule: {0}", ex);
        }
    }

    private sealed class StoreFile
    {
        [System.Text.Json.Serialization.JsonRequired]
        public List<StoreBlock>? Blocks { get; set; }
        public bool Enabled { get; set; } = true;
        public DateOnly? SkippedDate { get; set; }
        public bool ResumePromptPending { get; set; }
    }

    private sealed class StoreBlock
    {
        public Guid Id { get; set; }
        public string? Day { get; set; }
        public string? Start { get; set; }
        public string? End { get; set; }
        public string? Mode { get; set; }
        public int Cycles { get; set; }
        public string? Name { get; set; }
    }
}
