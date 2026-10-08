using Island.Core.Abstractions;
using Island.Core.Clipboard;
using Island.Core.Configuration;
using Island.Core.Pomodoro;
using Island.Core.Shelf;

namespace Island.App.Widgets;

/// <summary>Everything a shelf widget may need. Built once in the composition root.</summary>
public sealed class ShelfContext(
    PomodoroTimer pomodoro,
    AngryPomodoro angry,
    FileTray fileTray,
    ClipboardHistory clipboard,
    IClipboardService clipboardService,
    IMediaService media,
    Func<IslandSettings> settings,
    Action<IslandSettings> applySettings)
{
    public PomodoroTimer Pomodoro { get; } = pomodoro;
    /// <summary>Angry mode for the focus session: locks the timer and guards distracting sites.</summary>
    public AngryPomodoro Angry { get; } = angry;
    public FileTray FileTray { get; } = fileTray;
    public ClipboardHistory Clipboard { get; } = clipboard;
    public IClipboardService ClipboardService { get; } = clipboardService;
    public IMediaService Media { get; } = media;
    public Func<IslandSettings> Settings { get; } = settings;
    /// <summary>Persists + applies new settings (updates the holder, saves JSON, re-applies to the window).</summary>
    public Action<IslandSettings> ApplySettings { get; } = applySettings;
}
