using Island.Core.Abstractions;

namespace Island.Core.Pomodoro;

/// <summary>
/// A repeating Pomodoro block on one weekday, from <see cref="Start"/> to <see cref="End"/> (exclusive). Each focus of
/// <see cref="FocusMinutes"/> is followed by a break of <see cref="BreakMinutes"/>; as many pairs as fit the block run.
/// </summary>
public sealed record PomodoroWeeklyBlock(
    Guid Id,
    DayOfWeek Day,
    TimeOnly Start,
    TimeOnly End,
    ScheduledStartMode Mode,
    string Name,
    int FocusMinutes = PomodoroTimer.DefaultFocusMinutes,
    int BreakMinutes = PomodoroTimer.DefaultBreakMinutes)
{
    /// <summary>Focus/break pairs that fit in the block.</summary>
    public int Cycles => PomodoroTimer.CyclesFor(End - Start, FocusMinutes, BreakMinutes);
}

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
/// Runs a recurring weekly Pomodoro routine. Each block starts a named Focus or Angry plan at its start time and stops
/// at its exclusive end time, even in the middle of a plan. Every plan is sized to the time left in the block, so it
/// ends before the block does. A one-day skip pauses the routine for the next local date, then asks whether to resume
/// on the following date. This service owns only sessions it started itself.
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
    // True while the session running for _active was started here. Only such a session is ended at the block's end.
    private bool _ownsSession;
    private DateOnly? _skippedDate;
    private bool _enabled;
    private bool _resumePromptPending;
    private int _generation;
    private bool _disposed;

    private const string OverlapMessage = "Esta faixa se sobrepõe a outra em pelo menos um dos dias selecionados.";

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

    /// <summary>Adds a block, rejecting empty names, invalid ranges, lengths that do not fit a pomodoro, and overlaps on the same weekday.</summary>
    public bool TryAdd(DayOfWeek day, TimeOnly start, TimeOnly end, ScheduledStartMode mode, int focusMinutes, int breakMinutes,
        string? name, out string error)
        => TryAddDays([day], start, end, mode, focusMinutes, breakMinutes, name, out error);

    /// <summary>Adds the same time block to several weekdays atomically.</summary>
    public bool TryAddDays(IEnumerable<DayOfWeek> days, TimeOnly start, TimeOnly end, ScheduledStartMode mode,
        int focusMinutes, int breakMinutes, string? name, out string error)
    {
        string cleanName = PomodoroProfiles.CleanText(name, 160);
        DayOfWeek[] selectedDays = days.Distinct().ToArray();
        if (cleanName.Length == 0)
        {
            error = "Dê um nome para esta faixa.";
            return false;
        }
        if (!ValidateBlock(selectedDays, start, end, mode, focusMinutes, breakMinutes, out error)) return false;

        lock (_gate)
        {
            if (_disposed) { error = "A rotina semanal foi encerrada."; return false; }
            if (OverlapsLocked(selectedDays, start, end, excludeId: null))
            {
                error = OverlapMessage;
                return false;
            }
            foreach (DayOfWeek day in selectedDays)
                _blocks.Add(new PomodoroWeeklyBlock(Guid.NewGuid(), day, start, end, mode, cleanName,
                    ClampMinutes(focusMinutes), ClampMinutes(breakMinutes)));
            SortBlocksLocked();
            SaveLocked();
            error = string.Empty;
        }

        Reconcile();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Replaces the block <paramref name="id"/> with the given day, times, mode and lengths, keeping its id. Validated like
    /// <see cref="TryAddDays"/>. A block that is running now keeps its session; the new times apply from then on.
    /// </summary>
    public bool TryUpdate(Guid id, DayOfWeek day, TimeOnly start, TimeOnly end, ScheduledStartMode mode,
        int focusMinutes, int breakMinutes, string? name, out string error)
    {
        string cleanName = PomodoroProfiles.CleanText(name, 160);
        if (cleanName.Length == 0)
        {
            error = "Dê um nome para esta faixa.";
            return false;
        }
        if (!ValidateBlock([day], start, end, mode, focusMinutes, breakMinutes, out error)) return false;

        lock (_gate)
        {
            if (_disposed) { error = "A rotina semanal foi encerrada."; return false; }
            int index = _blocks.FindIndex(block => block.Id == id);
            if (index < 0)
            {
                error = "Esta faixa não existe mais.";
                return false;
            }
            if (OverlapsLocked([day], start, end, excludeId: id))
            {
                error = OverlapMessage;
                return false;
            }
            _blocks[index] = new PomodoroWeeklyBlock(id, day, start, end, mode, cleanName,
                ClampMinutes(focusMinutes), ClampMinutes(breakMinutes));
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
                // The block's end, a disabled routine or a switch to another block ends the session it started, even mid-plan.
                if (_active is not null && _ownsSession)
                {
                    stop = true;
                    _ownsSession = false;
                }
                _active = desired;
                start = desired;
                changed = true;
            }
            else if (desired is not null)
            {
                // The same block, edited in place: its session keeps running and the new end is armed below.
                _active = desired;
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
        lock (_gate)
        {
            if (_disposed) return;
            active = _active;
            DateTime now = _clock();
            if (!_enabled || _resumePromptPending || _skippedDate == DateOnly.FromDateTime(now)
                || active is null || active.Day != now.DayOfWeek
                || TimeOnly.FromDateTime(now) < active.Start || TimeOnly.FromDateTime(now) >= active.End) return;
        }

        // Keep the selected mode and name active for the entire time block, even when a plan ends early.
        StartManagedSession(active);
    }

    private void StartManagedSession(PomodoroWeeklyBlock block)
    {
        int cycles;
        lock (_gate)
        {
            if (_disposed || _active?.Id != block.Id || !_enabled || _resumePromptPending
                || _skippedDate == DateOnly.FromDateTime(_clock())) return;
            DateTime now = _clock();
            TimeOnly time = TimeOnly.FromDateTime(now);
            if (block.Day != now.DayOfWeek || time < block.Start || time >= block.End) return;

            // Only the pairs that still fit before the block ends, so the plan never runs past the block.
            cycles = PomodoroTimer.CyclesFor(block.End - time, block.FocusMinutes, block.BreakMinutes);
            if (cycles < 1) return;
        }
        // Like a one-off scheduled start, a weekly block never takes ownership from an existing Angry session.
        if (_angry.IsSessionActive) return;

        _timer.Reset();
        if (_timer.Phase != PomodoroPhase.Focus) _timer.SetPhase(PomodoroPhase.Focus);
        lock (_gate) _ownsSession = true;
        if (block.Mode == ScheduledStartMode.Angry)
            _angry.EngageScheduled(cycles, block.Name, block.FocusMinutes, block.BreakMinutes);
        else
            _timer.StartPlan(cycles, block.Name, block.FocusMinutes, block.BreakMinutes);
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

    private bool OverlapsLocked(IEnumerable<DayOfWeek> days, TimeOnly start, TimeOnly end, Guid? excludeId) =>
        days.Any(day => _blocks.Any(existing => existing.Id != excludeId && existing.Day == day
            && start < existing.End && existing.Start < end));

    private void SortBlocksLocked() => _blocks.Sort((a, b) =>
    {
        int day = WeekOrder(a.Day).CompareTo(WeekOrder(b.Day));
        return day != 0 ? day : a.Start.CompareTo(b.Start);
    });

    private static bool ValidateBlock(DayOfWeek[] days, TimeOnly start, TimeOnly end, ScheduledStartMode mode,
        int focusMinutes, int breakMinutes, out string error)
    {
        if (days.Length == 0)
        {
            error = "Escolha pelo menos um dia da semana.";
            return false;
        }
        if (days.Any(day => !Enum.IsDefined(day)) || !Enum.IsDefined(mode))
        {
            error = "Escolha um dia da semana e um modo válidos.";
            return false;
        }
        if (start >= end)
        {
            error = "O horário final precisa ser depois do horário inicial no mesmo dia.";
            return false;
        }
        if (focusMinutes < PomodoroTimer.MinMinutes || focusMinutes > PomodoroTimer.MaxMinutes
            || breakMinutes < PomodoroTimer.MinMinutes || breakMinutes > PomodoroTimer.MaxMinutes)
        {
            error = $"Foco e pausa precisam ter entre {PomodoroTimer.MinMinutes} e {PomodoroTimer.MaxMinutes} minutos.";
            return false;
        }
        if (PomodoroTimer.CyclesFor(end - start, focusMinutes, breakMinutes) < 1)
        {
            error = $"A faixa precisa caber ao menos um pomodoro de {focusMinutes + breakMinutes} minutos (foco + pausa).";
            return false;
        }

        error = string.Empty;
        return true;
    }

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
            var normalized = block with { Id = id, FocusMinutes = ClampMinutes(block.FocusMinutes), BreakMinutes = ClampMinutes(block.BreakMinutes), Name = name };
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

    private static int ClampMinutes(int minutes) => Math.Clamp(minutes, PomodoroTimer.MinMinutes, PomodoroTimer.MaxMinutes);

    private static int WeekOrder(DayOfWeek day) => ((int)day + 6) % 7;
}
