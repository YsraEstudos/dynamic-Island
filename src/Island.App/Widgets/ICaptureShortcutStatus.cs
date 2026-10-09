namespace Island.App.Widgets;

/// <summary>Whether the Capture shortcuts (Ctrl+Alt+P and Ctrl+Alt+R) could be registered. Lets the widget explain a conflict.</summary>
public interface ICaptureShortcutStatus
{
    bool PrintAvailable { get; }

    bool RecordAvailable { get; }

    event Action? AvailabilityChanged;
}
