using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Island.Core.Clipboard;

namespace Island.App.Widgets;

/// <summary>
/// UI-side view of one clipboard item. Its identity (<see cref="Id"/>) is stable for the item's lifetime so the panel can
/// keep the same card while the list changes. The image is decoded on first request, not when the list is built.
/// </summary>
public sealed class ClipboardEntry : ObservableObject
{
    private const int ImageDecodeWidth = 400;

    private readonly Action _copy;
    private readonly Action _delete;
    private bool _isLatest;
    private BitmapImage? _image;
    private bool _imageDecoded;

    public ClipboardEntry(ClipboardItem item, Action copy, Action delete)
    {
        Id = item.Id;
        Kind = item.Kind;
        Text = item.Text ?? string.Empty;
        ImageBytes = item.ImageBytes;
        At = item.At;
        _copy = copy;
        _delete = delete;
    }

    public Guid Id { get; }
    public ClipboardKind Kind { get; }
    /// <summary>Text, link or colour value. Empty for images.</summary>
    public string Text { get; }
    public byte[]? ImageBytes { get; }
    public DateTimeOffset At { get; }

    /// <summary>True for the newest item (drawn with the selected outline).</summary>
    public bool IsLatest
    {
        get => _isLatest;
        set => SetProperty(ref _isLatest, value);
    }

    /// <summary>Puts this item back on the system clipboard and collapses the island.</summary>
    public void Copy() => _copy();

    /// <summary>Removes this item from the history.</summary>
    public void Delete() => _delete();

    /// <summary>Decoded, frozen preview of an image item (null for other kinds or undecodable bytes).</summary>
    public BitmapImage? GetImage()
    {
        if (!_imageDecoded)
        {
            _imageDecoded = true;
            _image = Decode(ImageBytes);
        }
        return _image;
    }

    private static BitmapImage? Decode(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;

        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = ImageDecodeWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // Undecodable image: the card falls back to the placeholder fill.
            return null;
        }
    }
}
