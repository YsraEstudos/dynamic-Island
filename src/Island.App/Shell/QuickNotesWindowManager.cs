using Island.App.ViewModels;
using Island.App.Widgets;
using Island.Core.Notes;

namespace Island.App.Shell;

/// <summary>Keeps a single quick-notes editor window alive at a time.</summary>
public sealed class QuickNotesWindowManager : IQuickNotesWindowHost
{
    private readonly QuickNotesService _service;
    private readonly Func<bool> _reduceAnimations;
    private QuickNotesWindow? _window;

    public QuickNotesWindowManager(QuickNotesService service, Func<bool> reduceAnimations)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _reduceAnimations = reduceAnimations ?? throw new ArgumentNullException(nameof(reduceAnimations));
    }

    public event Action<bool>? HotkeyConflictChanged;

    public bool HotkeyConflict { get; private set; }

    public async void OpenForCapture()
    {
        try
        {
            var window = GetOrCreateWindow();
            window.ShowOrActivate();
            await window.BeginNewNoteAndFocusAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not open quick notes for capture");
        }
    }

    public void OpenNotes() => GetOrCreateWindow().ShowOrActivate();

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
}
