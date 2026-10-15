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
    private bool _editingEnabled = true;
    private Task _pendingWindowOperation = Task.CompletedTask;

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
        Run("open a quick note", () => TrackWindowOperation(GetOrCreateWindow().ShowNoteAsync(id, source)));

    public void OpenNewNote(Func<Rect?> source) =>
        Run("start a new quick note", () => TrackWindowOperation(GetOrCreateWindow().ShowNewNoteAsync(source)));

    public void SetHotkeyConflict(bool conflict)
    {
        if (HotkeyConflict == conflict) return;

        HotkeyConflict = conflict;
        _window?.SetHotkeyConflict(conflict);
        HotkeyConflictChanged?.Invoke(conflict);
    }

    public async Task FlushPendingSaveAsync()
    {
        await _pendingWindowOperation;
        if (_captureWindow is { } capture && !await capture.SaveBeforeShutdownAsync())
            throw new System.IO.IOException("The quick-capture draft could not be saved.");
        if (_window is { } window) await window.FlushPendingSaveAsync();
    }

    public void SetEditingEnabled(bool enabled)
    {
        _editingEnabled = enabled;
        if (_window is { } window) window.IsEnabled = enabled;
        if (_captureWindow is { } capture) capture.IsEnabled = enabled;
    }

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
        if (!_editingEnabled) return;
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
            () => TrackWindowOperation(GetOrCreateWindow().ShowNewNoteAsync(openFrom, draftText, closeTo)));
    }

    private QuickNotesWindow GetOrCreateWindow()
    {
        if (!_editingEnabled) throw new InvalidOperationException("Notes are being saved for shutdown.");
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

    private void TrackWindowOperation(Task operation) =>
        _pendingWindowOperation = _pendingWindowOperation.IsCompleted ? operation : Task.WhenAll(_pendingWindowOperation, operation);

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
