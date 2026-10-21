using Island.Core.Abstractions;
using Island.Core.Budgets;
using Island.Core.Capture;
using Island.Core.Clipboard;
using Island.Core.Configuration;
using Island.Core.Calendar;
using Island.Core.GameNotes;
using Island.Core.Notes;
using Island.Core.Performance;
using Island.Core.Pomodoro;
using Island.Core.Shelf;
using Island.Core.Tools;
using Island.Core.Tools.FocusSounds;

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
    GameNotesService? gameNotes = null,
    IGameNotesWindowHost? gameNotesHost = null,
    PerformanceMonitor? performance = null,
    CaptureController? capture = null,
    ICaptureShortcutStatus? captureShortcuts = null,
    IAudioMixerService? mixer = null,
    Func<IFocusAudioOutput>? focusAudioOutput = null,
    PomodoroWeeklySchedule? weeklySchedule = null,
    AngryDesktopLock? angryDesktop = null,
    IVirtualDesktopService? desktops = null) : IDisposable
{
    public PomodoroTimer Pomodoro { get; } = pomodoro;
    /// <summary>Angry mode for the focus session: locks the timer and guards distracting sites.</summary>
    public AngryPomodoro Angry { get; } = angry;
    /// <summary>Scheduled start: a plan (Focus or Angry) that begins by itself at a clock time.</summary>
    public PomodoroSchedule Schedule { get; } = schedule;
    /// <summary>Recurring weekly Pomodoro blocks; null only in isolated widget fixtures.</summary>
    public PomodoroWeeklySchedule? WeeklySchedule { get; } = weeklySchedule;
    /// <summary>Holds Angry focus on the chosen Windows virtual desktop; null in isolated widget fixtures.</summary>
    public AngryDesktopLock? AngryDesktop { get; } = angryDesktop;
    /// <summary>Lists and reads the Windows virtual desktops for the Angry desktop picker; null in isolated widget fixtures.</summary>
    public IVirtualDesktopService? Desktops { get; } = desktops;
    public FileTray FileTray { get; } = fileTray;
    public ClipboardHistory Clipboard { get; } = clipboard;
    public IClipboardService ClipboardService { get; } = clipboardService;
    public IMediaService Media { get; } = media;
    public CalendarAgenda Calendar { get; } = calendar;
    public QuickNotesService QuickNotes { get; } = quickNotes;
    public IQuickNotesWindowHost QuickNotesHost { get; } = quickNotesHost;
    public BudgetBook? Budgets { get; } = budgets;
    public IBudgetWindowHost? BudgetHost { get; } = budgetHost;
    public GameNotesService? GameNotes { get; } = gameNotes;
    public IGameNotesWindowHost? GameNotesHost { get; } = gameNotesHost;
    /// <summary>Temperature and load sampler for the Desempenho widget. Samples only while the widget is on screen or alerts are on.</summary>
    public PerformanceMonitor? Performance { get; } = performance;
    /// <summary>Screenshot and recording use cases for the Capture widget.</summary>
    public CaptureController? Capture { get; } = capture;
    /// <summary>Whether the Ctrl+Alt+P and Ctrl+Alt+R shortcuts are registered, so the widget can explain a conflict.</summary>
    public ICaptureShortcutStatus? CaptureShortcuts { get; } = captureShortcuts;
    public Func<IslandSettings> Settings { get; } = settings;
    /// <summary>Persists + applies new settings (updates the holder, saves JSON, re-applies to the window).</summary>
    public Action<IslandSettings> ApplySettings { get; } = applySettings;
    /// <summary>Per-app audio sessions for the Mixer widget.</summary>
    public IAudioMixerService? Mixer { get; } = mixer;
    /// <summary>Opens the speaker output for the focus sounds. Silent when none is given (tests, --demo).</summary>
    public Func<IFocusAudioOutput> FocusAudioOutput { get; } = focusAudioOutput ?? (() => new NullFocusAudioOutput());
    /// <summary>Long-lived models of the small tool widgets (calculator, counter, timers, focus sounds, text analyzer).</summary>
    public ToolRegistry Tools { get; } = new();

    /// <summary>Disposed with the service provider on exit: flushes the counter and stops focus sounds.</summary>
    public void Dispose() => Tools.Dispose();
}
