using Island.Core.Abstractions;
using Island.Core.Budgets;
using Island.Core.Clipboard;
using Island.Core.Configuration;
using Island.Core.Calendar;
using Island.Core.Notes;
using Island.Core.Performance;
using Island.Core.Pomodoro;
using Island.Core.Shelf;

namespace Island.App.Widgets;

/// <summary>Everything a shelf widget may need. Built once in the composition root.</summary>
public sealed class ShelfContext(
    PomodoroTimer pomodoro,
    AngryPomodoro angry,
    PomodoroSchedule schedule,
    FileTray fileTray,
    ClipboardHistory clipboard,
    IClipboardService clipboardService,
    IMediaService media,
    CalendarAgenda calendar,
    QuickNotesService quickNotes,
    IQuickNotesWindowHost quickNotesHost,
    Func<IslandSettings> settings,
    Action<IslandSettings> applySettings,
    BudgetBook? budgets = null,
    IBudgetWindowHost? budgetHost = null,
    PerformanceMonitor? performance = null)
{
    public PomodoroTimer Pomodoro { get; } = pomodoro;
    /// <summary>Angry mode for the focus session: locks the timer and guards distracting sites.</summary>
    public AngryPomodoro Angry { get; } = angry;
    /// <summary>Scheduled start: a plan (Focus or Angry) that begins by itself at a clock time.</summary>
    public PomodoroSchedule Schedule { get; } = schedule;
    public FileTray FileTray { get; } = fileTray;
    public ClipboardHistory Clipboard { get; } = clipboard;
    public IClipboardService ClipboardService { get; } = clipboardService;
    public IMediaService Media { get; } = media;
    public CalendarAgenda Calendar { get; } = calendar;
    public QuickNotesService QuickNotes { get; } = quickNotes;
    public IQuickNotesWindowHost QuickNotesHost { get; } = quickNotesHost;
    public BudgetBook? Budgets { get; } = budgets;
    public IBudgetWindowHost? BudgetHost { get; } = budgetHost;
    /// <summary>Temperature and load sampler for the Desempenho widget. Samples only while the widget is on screen or alerts are on.</summary>
    public PerformanceMonitor? Performance { get; } = performance;
    public Func<IslandSettings> Settings { get; } = settings;
    /// <summary>Persists + applies new settings (updates the holder, saves JSON, re-applies to the window).</summary>
    public Action<IslandSettings> ApplySettings { get; } = applySettings;
}
