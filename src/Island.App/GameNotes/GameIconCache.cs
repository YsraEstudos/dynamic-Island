using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island.App.GameNotes;

/// <summary>
/// The game's own icon, read from its executable and cached per path. A failed read is cached too, so a missing or
/// protected file is not retried on every redraw. Use on the UI thread.
/// </summary>
public sealed class GameIconCache
{
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Frozen 32 px icon, or null when the executable has no readable icon.</summary>
    public ImageSource? Get(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;

        if (_icons.TryGetValue(exePath, out ImageSource? cached)) return cached;

        ImageSource? icon = Load(exePath);
        _icons[exePath] = icon;
        return icon;
    }

    private static ImageSource? Load(string exePath)
    {
        try
        {
            // The WinForms icon is only a handle source: WPF copies the pixels, so it can be disposed right away.
            using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return null;

            ImageSource source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            // Icons are cosmetic. The widget falls back to its glyph.
            return null;
        }
    }
}
