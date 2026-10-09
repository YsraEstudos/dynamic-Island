using System.Windows;

namespace Island.App.Widgets;

/// <summary>
/// UI-independent entry points for the quick-notes windows. The overloads taking a <c>source</c> callback receive the
/// widget's current screen rectangle (DIPs) so the window can grow out of the widget; it is asked again when closing.
/// </summary>
public interface IQuickNotesWindowHost
{
    event Action<bool>? HotkeyConflictChanged;

    bool HotkeyConflict { get; }

    /// <summary>Opens the small capture window (global hotkey: no widget to grow from).</summary>
    void OpenForCapture();

    /// <summary>Opens the small capture window growing out of the widget's capture bar.</summary>
    void OpenForCapture(Func<Rect?> source);

    void OpenNotes();

    /// <summary>Opens the full notes app growing out of the widget.</summary>
    void OpenNotes(Func<Rect?> source);

    /// <summary>Opens the full notes app on a given note.</summary>
    void OpenNote(Guid id, Func<Rect?> source);

    /// <summary>Opens the full notes app on a blank draft with the title focused.</summary>
    void OpenNewNote(Func<Rect?> source);

    void SetHotkeyConflict(bool conflict);
}
