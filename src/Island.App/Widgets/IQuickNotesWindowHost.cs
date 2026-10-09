namespace Island.App.Widgets;

/// <summary>UI-independent entry points for opening the quick-notes window.</summary>
public interface IQuickNotesWindowHost
{
    event Action<bool>? HotkeyConflictChanged;

    bool HotkeyConflict { get; }

    void OpenForCapture();

    void OpenNotes();

    void SetHotkeyConflict(bool conflict);
}
