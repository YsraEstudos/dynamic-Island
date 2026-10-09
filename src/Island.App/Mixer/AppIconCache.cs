using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island.App.Mixer;

/// <summary>
/// Small icons of executables, one per path. Each icon is copied into a frozen WPF bitmap and its HICON is destroyed at
/// once, so no GDI handle outlives the lookup. Lookups that fail are cached too, so a missing icon is not retried on every
/// refresh. Used from the UI thread only.
/// </summary>
internal static class AppIconCache
{
    private const int MaxEntries = 64;
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the icon for an executable path, or null when there is none.</summary>
    public static ImageSource? Get(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;

        if (Cache.TryGetValue(executablePath, out ImageSource? cached)) return cached;

        if (Cache.Count >= MaxEntries) Cache.Clear();   // a plain bound: a mixer shows a handful of apps at a time
        ImageSource? icon = Extract(executablePath);
        Cache[executablePath] = icon;
        return icon;
    }

    private static ImageSource? Extract(string path)
    {
        var info = new SHFILEINFO();
        try
        {
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON) == IntPtr.Zero
                || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;   // unreadable executable: the row shows the generic glyph
        }
        finally
        {
            if (info.hIcon != IntPtr.Zero) DestroyIcon(info.hIcon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
