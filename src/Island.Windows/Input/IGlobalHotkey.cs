namespace Island.Windows.Input;

/// <summary>Small seam for registering and testing a system-wide shortcut.</summary>
public interface IGlobalHotkey : IDisposable
{
    /// <summary>Raised on the hotkey's message-window thread.</summary>
    event Action? Pressed;

    /// <summary>Returns false when Windows cannot register the key combination.</summary>
    bool Register();
}
