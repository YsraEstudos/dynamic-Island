namespace Island.Core.Models;

/// <summary>Visual states the single island shape can morph between.</summary>
public enum IslandMode
{
    /// <summary>Tiny pill the user flicked away. Passive events (volume, media, notices) never leave it; a click restores Compact.</summary>
    Mini,
    Compact,
    Volume,
    MediaPreview,
    /// <summary>User-opened widget shelf (now playing, pomodoro, calendar, file tray...).</summary>
    Expanded,
    /// <summary>Shelf in edit mode: widgets can be removed/added from the bottom tray.</summary>
    Customize,
    /// <summary>Wide clipboard history panel.</summary>
    Clipboard,
    /// <summary>Temporary toast (pomodoro finished, etc.).</summary>
    Notice,
}
