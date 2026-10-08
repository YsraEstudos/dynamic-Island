namespace Island.Core.Clipboard;

public interface IClipboardService : IDisposable
{
    /// <summary>A new item was copied by the user (arbitrary thread). Items flagged as private by their source (password managers) are never raised, nor are items this app itself put on the clipboard.</summary>
    event EventHandler<ClipboardItem>? ItemCaptured;

    /// <summary>Starts listening. Must be safe to call once.</summary>
    void Start();

    /// <summary>Puts content on the system clipboard WITHOUT re-raising ItemCaptured.</summary>
    void SetText(string text);
    void SetImage(byte[] imageBytes);
}
