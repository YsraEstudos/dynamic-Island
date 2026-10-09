using System.Windows;
using Island.App.ViewModels;
using Island.App.Widgets;
using Island.Core.Notes;

namespace Island.App.Shell;

/// <summary>Keeps a single notes app window and a single quick-capture window alive at a time.</summary>
public sealed class QuickNotesWindowManager : IQuickNotesWindowHost
{
    private readonly QuickNotesService _service;
    private readonly Func<bool> _reduceAnimations;
    private QuickNotesWindow? _window;
    private QuickCaptureWindow? _captureWindow;

    public QuickNotesWindowManager(QuickNotesService service, Func<bool> reduceAnimations)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _reduceAnimations = reduceAnimations ?? throw new ArgumentNullException(nameof(reduceAnimations));
    }

    public event Action<bool>? HotkeyConflictChanged;

    public bool HotkeyConflict { get; private set; }

    public void OpenForCapture() => OpenCapture(null);

    public void OpenForCapture(Func<Rect?> source) => OpenCapture(source);

    public void OpenNotes() => Run("open quick notes", () => GetOrCreateWindow().ShowOrActivate(null));

    public void OpenNotes(Func<Rect?> source) =>
        Run("open quick notes", () => GetOrCreateWindow().ShowOrActivate(source));

    public void OpenNote(Guid id, Func<Rect?> source) =>
        Run("open a quick note", () => _ = GetOrCreateWindow().ShowNoteAsync(id, source));

    public void OpenNewNote(Func<Rect?> source) =>
        Run("start a new quick note", () => _ = GetOrCreateWindow().ShowNewNoteAsync(source));

    public void SetHotkeyConflict(bool conflict)
    {
        if (HotkeyConflict == conflict) return;

        HotkeyConflict = conflict;
        _window?.SetHotkeyConflict(conflict);
        HotkeyConflictChanged?.Invoke(conflict);
    }

    public Task FlushPendingSaveAsync() => _window?.FlushPendingSaveAsync() ?? Task.CompletedTask;

    public bool HasSaveError => _window?.HasSaveError == true;

    public void CloseForShutdown()
    {
        QuickNotesWindow? window = _window;
        _window = null;
        window?.CloseForShutdown();

        QuickCaptureWindow? capture = _captureWindow;
        _captureWindow = null;
        capture?.CloseForShutdown();
    }

    private void OpenCapture(Func<Rect?>? source)
    {
        Run("open quick capture", () =>
        {
            if (_captureWindow is not { } capture)
            {
                capture = new QuickCaptureWindow(_service, _reduceAnimations, OpenFromCapture);
                capture.Closed += (_, _) => _captureWindow = null;
                _captureWindow = capture;
            }

            capture.ShowCapture(source);
        });
    }

    /// <summary>The capture window hands over to the full app ("Abrir no app"), carrying the typed text along.</summary>
    private void OpenFromCapture(string draftText, Func<Rect?> openFrom, Func<Rect?>? closeTo)
    {
        Run("hand capture over to the notes app",
            () => _ = GetOrCreateWindow().ShowNewNoteAsync(openFrom, draftText, closeTo));
    }

    private QuickNotesWindow GetOrCreateWindow()
    {
        if (_window is { } existing) return existing;

        var window = new QuickNotesWindow(new QuickNotesViewModel(_service), _reduceAnimations);
        window.SetHotkeyConflict(HotkeyConflict);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window)) _window = null;
        };
        _window = window;
        return window;
    }

    private static void Run(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not {Action}", what);
        }
    }
}
