namespace Island.Core.Clipboard;

public enum ClipboardKind { Text, Link, Image, Color }

/// <summary>One clipboard entry. Text is set for Text/Link/Color. ImageBytes is any WPF-decodable image (PNG/BMP) for Image.</summary>
public sealed record ClipboardItem(Guid Id, ClipboardKind Kind, string? Text, byte[]? ImageBytes, DateTimeOffset At)
{
    public static ClipboardItem FromText(string text, DateTimeOffset? at = null)
        => new(Guid.NewGuid(), ClipboardHistory.Classify(text), text, null, at ?? DateTimeOffset.Now);
    public static ClipboardItem FromImage(byte[] bytes, DateTimeOffset? at = null)
        => new(Guid.NewGuid(), ClipboardKind.Image, null, bytes, at ?? DateTimeOffset.Now);
}
