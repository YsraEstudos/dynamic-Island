using Island.Core.Clipboard;

namespace Island.Core.Fakes;

/// <summary>
/// In-memory <see cref="IClipboardService"/>. <see cref="Raise"/> simulates a user copy. SetText and SetImage record the
/// write and never raise <see cref="ItemCaptured"/>, matching the real service.
/// </summary>
public sealed class FakeClipboardService : IClipboardService
{
    private readonly object _gate = new();
    private readonly List<string> _texts = new();
    private readonly List<byte[]> _images = new();

    public event EventHandler<ClipboardItem>? ItemCaptured;

    public int StartCount { get; private set; }

    /// <summary>Texts written through <see cref="SetText"/>, in call order.</summary>
    public IReadOnlyList<string> SetTextCalls
    {
        get
        {
            lock (_gate) return _texts.ToArray();
        }
    }

    /// <summary>Images written through <see cref="SetImage"/>, in call order.</summary>
    public IReadOnlyList<byte[]> SetImageCalls
    {
        get
        {
            lock (_gate) return _images.ToArray();
        }
    }

    public void Start() => StartCount++;

    /// <summary>Simulates the user copying <paramref name="item"/>. Raised synchronously on the calling thread.</summary>
    public void Raise(ClipboardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ItemCaptured?.Invoke(this, item);
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate) _texts.Add(text);
    }

    public void SetImage(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        lock (_gate) _images.Add(imageBytes);
    }

    public void Dispose() { }
}
