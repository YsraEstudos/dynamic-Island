using Island.Core.Abstractions;

namespace Island.Core.Pomodoro;

/// <summary>A repeating Pomodoro block on one weekday. The end time is exclusive.</summary>
public sealed record PomodoroWeeklyBlock(
    Guid Id,
    DayOfWeek Day,
    TimeOnly Start,
    TimeOnly End,
    ScheduledStartMode Mode,
    int Cycles,
    string Name);

/// <summary>Persisted weekly routine and its temporary pause state.</summary>
public sealed record PomodoroWeeklyScheduleState(
    IReadOnlyList<PomodoroWeeklyBlock> Blocks,
    bool Enabled,
    DateOnly? SkippedDate,
    bool ResumePromptPending);

public interface IPomodoroWeeklyScheduleStore
{
    PomodoroWeeklyScheduleState Load();
    void Save(PomodoroWeeklyScheduleState state);
}

/// <summary>
/// Runs a recurring weekly Pomodoro routine. Each block starts a named Focus or Angry plan at its start time and
/// stops starting new plans at the exclusive end time, allowing an in-progress plan to finish. A one-day skip pauses
/// the routine for the next local date, then asks whether to resume on the following date. This service owns only
/// sessions it started itself.
/// </summary>
public sealed class PomodoroWeeklySchedule : IDisposable
{
    private readonly PomodoroTimer _timer;
    private readonly AngryPomodoro _angry;
    private readonly IIslandScheduler _scheduler;
    private readonly Func<DateTime> _clock;
    private readonly IPomodoroWeeklyScheduleStore? _store;
    private readonly object _gate = new();
    private readonly List<PomodoroWeeklyBlock> _blocks = [];
    private IDisposable? _handle;
    private PomodoroWeeklyBlock? _active;
    private PomodoroWeeklyBlock? _finishingBlock;
    private DateOnly? _skippedDate;
    private bool _enabled;
    private bool _resumePromptPending;
    private int _generation;
    private bool _disposed;

    public PomodoroWeeklySchedule(PomodoroTimer timer, AngryPomodoro angry, IIslandScheduler scheduler,
        Func<DateTime>? clock = null, IPomodoroWeeklyScheduleStore? store = null)
    {
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
        _angry = angry ?? throw new ArgumentNullException(nameof(angry));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _clock = clock ?? (() => DateTime.Now);
        _store = store;

        PomodoroWeeklyScheduleState saved = _store?.Load()
            ?? new PomodoroWeeklyScheduleState([], Enabled: true, SkippedDate: null, ResumePromptPending: false);
        _enabled = saved.Enabled;
        _skippedDate = saved.SkippedDate;
        _resumePromptPending = saved.ResumePromptPending;
        _blocks.AddRange(Normalize(saved.Blocks));
        _timer.Transitioned += OnTimerTransitioned;
        Reconcile();
    }

    public event Action? Changed;
    public event Action? ResumeConfirmationRequested;

    public IReadOnlyList<PomodoroWeeklyBlock> Blocks
    {
        get { lock (_gate) return _blocks.ToArray(); }
    }

    public bool Enabled
    {
        get { lock (_gate) return _enabled; }
    }

    public DateOnly? SkippedDate
    {
        get { lock (_gate) return _skippedDate; }
    }

    public bool HasPendingResumePrompt
    {
        get { lock (_gate) return _resumePromptPending; }
    }

    /// <summary>Re-evaluates local clock-based blocks after a clock change or system resume.</summary>
    public void RefreshNow() => Reconcile();

    /// <summary>Adds a block, rejecting empty names, invalid ranges, and overlaps on the same weekday.</summary>
    public bool TryAdd(DayOfWeek day, TimeOnly start, TimeOnly end, ScheduledStartMode mode, int cycles,
        string? name, out string error)
        => TryAddDays([day], start, end, mode, cycles, name, out error);

    /// <summary>Adds the same time block to several weekdays atomically.</summary>
    public bool TryAddDays(IEnumerable<DayOfWeek> days, TimeOnly start, TimeOnly end, ScheduledStartMode mode,
        int cycles, string? name, out string error)
    {
        string cleanName = PomodoroProfiles.CleanText(name, 160);
        DayOfWeek[] selectedDays = days.Distinct().ToArray();
        if (cleanName.Length == 0)
        {
            error = "Dê um nome para esta faixa.";
            return false;
        }
        if (selectedDays.Length == 0 || selectedDays.Any(day => !Enum.IsDefined(day))
            || !Enum.IsDefined(mode) || start >= end)
        {
            error = selectedDays.Length == 0 ? "Escolha pelo menos um dia da semana."
                : "O horário final precisa ser depois do horário inicial no mesmo dia.";
            return false;
        }

        lock (_gate)
        {
            if (_disposed) { error = "A rotina semanal foi encerrada."; return false; }
            if (selectedDays.Any(day => _blocks.Any(existing => existing.Day == day && start < existing.End && existing.Start < end)))
            {
                error = "Esta faixa se sobrepõe a outra em pelo menos um dos dias selecionados.";
                return false;
            }
            foreach (DayOfWeek day in selectedDays)
                _blocks.Add(new PomodoroWeeklyBlock(Guid.NewGuid(), day, start, end, mode,
                    Math.Clamp(cycles, PomodoroTimer.MinCycles, PomodoroTimer.MaxCycles), cleanName));
            SortBlocksLocked();
            SaveLocked();
            error = string.Empty;
        }

        Reconcile();
        Changed?.Invoke();
        return true;
    }

    public bool Remove(Guid id)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            int index = _blocks.FindIndex(block => block.Id == id);
            if (index < 0) return false;
            _blocks.RemoveAt(index);
            SaveLocked();
        }
        Reconcile();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Enables or completely disables the routine. Enabling clears a one-day pause.</summary>
    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_disposed || (_enabled == enabled && _skippedDate is null && !_resumePromptPending)) return;
            _enabled = enabled;
            _skippedDate = null;
            _resumePromptPending = false;
            SaveLocked();
        }
        Reconcile();
        Changed?.Invoke();
    }

    /// <summary>Skips tomorrow only. At the start of the following day the user is asked whether to resume.</summary>
    public void SkipNextDay()
    {
        lock (_gate)
        {
            if (_disposed || !_enabled) return;
            _skippedDate = DateOnly.FromDateTime(_clock()).AddDays(1);
            _resumePromptPending = false;
            SaveLocked();
        }
        Reconcile();
        Changed?.Invoke();
    }

    /// <summary>Answers the one-time resume question. No keeps the whole routine disabled.</summary>
    public void AnswerResumePrompt(bool resume)
    {
        lock (_gate)
        {
            if (_disposed || !_resumePromptPending) return;
            _enabled = resume;
            _skippedDate = null;
            _resumePromptPending = false;
            SaveLocked();
        }
        Reconcile();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelLocked();
            _active = null;
        }
        _timer.Transitioned -= OnTimerTransitioned;
    }

    private void Reconcile()
    {
        bool timerInProgress = _timer.IsRunning || _timer.PlanActive || _angry.IsSessionActive;
        PomodoroWeeklyBlock? start = null;
        bool stop = false;
        bool prompt = false;
        bool changed = false;

        lock (_gate)
        {
            if (_disposed) return;
            DateTime now = _clock();
            DateOnly today = DateOnly.FromDateTime(now);
            if (_enabled && _skippedDate is { } skipped && today > skipped && !_resumePromptPending)
            {
                _resumePromptPending = true;
                SaveLocked();
                prompt = true;
            }

            PomodoroWeeklyBlock? desired = _enabled && !_resumePromptPending && _skippedDate != today
                ? FindActiveLocked(today, TimeOnly.FromDateTime(now))
                : null;

            if (_active?.Id != desired?.Id)
            {
                if (_active is { } previous)
                {
                    bool endedNaturally = desired is null && _enabled && !_resumePromptPending
                        && _skippedDate != today && _blocks.Any(block => block.Id == previous.Id)
                        && previous.Day == today.DayOfWeek && TimeOnly.FromDateTime(now) >= previous.End;
                    if (endedNaturally && timerInProgress) _finishingBlock = previous;
                    else stop = true;
                }
                _active = desired;
                start = desired;
                if (desired is not null && _finishingBlock is not null)
                {
                    stop = true;
                    _finishingBlock = null;
                }
                changed = true;
            }
            else if (_active is null && _finishingBlock is not null
                && (!_enabled || _resumePromptPending || _skippedDate == today))
            {
                stop = true;
                _finishingBlock = null;
                changed = true;
            }

            ArmLocked(now, today, desired);
        }

        if (stop) StopManagedSession();
        if (start is not null) StartManagedSession(start);
        if (prompt) ResumeConfirmationRequested?.Invoke();
        if (changed || prompt) Changed?.Invoke();
    }

    private PomodoroWeeklyBlock? FindActiveLocked(DateOnly day, TimeOnly time) =>
        _blocks.FirstOrDefault(block => block.Day == day.DayOfWeek && block.Start <= time && time < block.End);

    private void OnTimerTransitioned(PomodoroTransition transition)
    {
        if (!transition.PlanFinished) return;
        PomodoroWeeklyBlock? active;
        bool wasFinishing;
        lock (_gate)
        {
            if (_disposed) return;
            wasFinishing = _finishingBlock is not null;
            if (wasFinishing) _finishingBlock = null;
            active = _active;
            DateTime now = _clock();
            if (wasFinishing || !_enabled || _resumePromptPending || _skippedDate == DateOnly.FromDateTime(now)
                || active is null || active.Day != now.DayOfWeek
                || TimeOnly.FromDateTime(now) < active.Start || TimeOnly.FromDateTime(now) >= active.End) return;
        }

        // Keep the selected mode and name active for the entire time block, even when a plan ends early.
        StartManagedSession(active);
    }

    private void StartManagedSession(PomodoroWeeklyBlock block)
    {
        lock (_gate)
        {
            if (_disposed || _active?.Id != block.Id || !_enabled || _resumePromptPending
                || _skippedDate == DateOnly.FromDateTime(_clock())) return;
            DateTime now = _clock();
            if (block.Day != now.DayOfWeek || TimeOnly.FromDateTime(now) < block.Start || TimeOnly.FromDateTime(now) >= block.End) return;
        }
        // Like a one-off scheduled start, a weekly block never takes ownership from an existing Angry session.
        if (_angry.IsSessionActive) return;

        _timer.Reset();
        if (_timer.Phase != PomodoroPhase.Focus) _timer.SetPhase(PomodoroPhase.Focus);
        if (block.Mode == ScheduledStartMode.Angry)
            _angry.EngageScheduled(block.Cycles, block.Name);
        else
            _timer.StartPlan(block.Cycles, block.Name);
    }

    private void StopManagedSession()
    {
        if (!_angry.EndScheduledSession()) _timer.Reset();
    }

    private void ArmLocked(DateTime now, DateOnly today, PomodoroWeeklyBlock? active)
    {
        CancelLocked();
        if (active is null && (!_enabled || _resumePromptPending)
            && (_skippedDate is null || _resumePromptPending)) return;
        if (active is null && _blocks.Count == 0 && _skippedDate is null) return;

        DateTime? next = null;
        if (active is not null)
        {
            next = today.ToDateTime(active.End);
        }
        else if (_enabled && !_resumePromptPending && _skippedDate != today && _blocks.Count > 0)
        {
            foreach (PomodoroWeeklyBlock block in _blocks)
            {
                int days = ((int)block.Day - (int)today.DayOfWeek + 7) % 7;
                DateTime occurrence = today.AddDays(days).ToDateTime(block.Start);
                if (occurrence <= now) occurrence = occurrence.AddDays(7);
                if (next is null || occurrence < next) next = occurrence;
            }
        }

        if (_enabled && _skippedDate is { } skipped && !_resumePromptPending)
        {
            DateTime promptAt = skipped.AddDays(1).ToDateTime(TimeOnly.MinValue);
            if (promptAt > now && (next is null || promptAt < next)) next = promptAt;
        }

        if (next is null) return;
        TimeSpan delay = next.Value - now;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        int generation = _generation;
        _handle = _scheduler.Schedule(delay, () => OnScheduledCheck(generation));
    }

    private void OnScheduledCheck(int generation)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation) return;
            _handle = null;
        }
        Reconcile();
    }

    private void CancelLocked()
    {
        _generation++;
        IDisposable? handle = _handle;
        _handle = null;
        handle?.Dispose();
    }

    private void SaveLocked() => _store?.Save(new PomodoroWeeklyScheduleState(_blocks.ToArray(), _enabled,
        _skippedDate, _resumePromptPending));

    private void SortBlocksLocked() => _blocks.Sort((a, b) =>
    {
        int day = WeekOrder(a.Day).CompareTo(WeekOrder(b.Day));
        return day != 0 ? day : a.Start.CompareTo(b.Start);
    });

    private static IReadOnlyList<PomodoroWeeklyBlock> Normalize(IEnumerable<PomodoroWeeklyBlock>? blocks)
    {
        var result = new List<PomodoroWeeklyBlock>();
        var ids = new HashSet<Guid>();
        foreach (PomodoroWeeklyBlock? block in blocks ?? [])
        {
            if (block is null || !Enum.IsDefined(block.Day) || !Enum.IsDefined(block.Mode) || block.Start >= block.End)
                continue;
            Guid id = block.Id == Guid.Empty || !ids.Add(block.Id) ? Guid.NewGuid() : block.Id;
            ids.Add(id);
            string name = PomodoroProfiles.CleanText(block.Name, 160);
            if (name.Length == 0) continue;
            var normalized = block with { Id = id, Cycles = Math.Clamp(block.Cycles, PomodoroTimer.MinCycles, PomodoroTimer.MaxCycles), Name = name };
            if (result.Any(existing => existing.Day == normalized.Day && normalized.Start < existing.End && existing.Start < normalized.End))
                continue;
            result.Add(normalized);
        }
        result.Sort((a, b) =>
        {
            int day = WeekOrder(a.Day).CompareTo(WeekOrder(b.Day));
            return day != 0 ? day : a.Start.CompareTo(b.Start);
        });
        return result;
    }

    private static int WeekOrder(DayOfWeek day) => ((int)day + 6) % 7;
}
