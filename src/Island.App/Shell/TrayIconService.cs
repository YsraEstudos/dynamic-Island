using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Imaging = System.Drawing.Imaging;
using WinForms = System.Windows.Forms;

namespace Island.App.Shell;

/// <summary>
/// Notification-area icon with a context menu (Settings, Pause island, Quit).
/// Must be created and disposed on the UI thread.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const int IconSize = 32;

    private readonly Action _openSettings;
    private readonly Action<bool> _setPaused;
    private readonly Action _exit;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly WinForms.ToolStripMenuItem _pauseItem;
    private readonly Drawing.Icon _icon;
    private readonly IntPtr _iconHandle;
    private bool _isPaused;
    private bool _disposed;

    public TrayIconService(Action openSettings, Action<bool> setPaused, Action exit)
    {
        _openSettings = openSettings ?? throw new ArgumentNullException(nameof(openSettings));
        _setPaused = setPaused ?? throw new ArgumentNullException(nameof(setPaused));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));

        // Icon: GDI handle owned by us; Icon.FromHandle does not own it, so DestroyIcon runs in Dispose.
        _iconHandle = CreateIconHandle();
        _icon = Drawing.Icon.FromHandle(_iconHandle);

        var settingsItem = new WinForms.ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => _openSettings();

        _pauseItem = new WinForms.ToolStripMenuItem("Pause island") { CheckOnClick = false };
        _pauseItem.Click += (_, _) => OnPauseClicked();

        var quitItem = new WinForms.ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => _exit();

        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(quitItem);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _icon,
            Text = "Dynamic Island",
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => _openSettings();
        _notifyIcon.Visible = true;
    }

    /// <summary>True when the island is paused (as shown by the menu check mark).</summary>
    public bool IsPaused => _isPaused;

    /// <summary>
    /// Re-adds the icon to the notification area. WinForms' NotifyIcon already re-creates itself on the
    /// "TaskbarCreated" broadcast; this explicit toggle is a safety net the island calls on the same signal.
    /// Idempotent, and must run on the UI thread.
    /// </summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Visible = true;
    }

    /// <summary>Updates the check mark without invoking the pause callback.</summary>
    public void SetPaused(bool paused)
    {
        if (_disposed)
        {
            return;
        }

        _isPaused = paused;
        _pauseItem.Checked = paused;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _notifyIcon.Icon = null;
        _notifyIcon.Dispose();

        _menu.Dispose();

        // _icon wraps _iconHandle without owning it, so the handle is released explicitly here.
        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }
    }

    private void OnPauseClicked()
    {
        if (_disposed)
        {
            return;
        }

        bool next = !_isPaused;
        SetPaused(next);
        _setPaused(next);
    }

    /// <summary>Renders a black capsule with a white outline and returns its GDI icon handle.</summary>
    private static IntPtr CreateIconHandle()
    {
        using var bitmap = new Drawing.Bitmap(IconSize, IconSize, Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(Drawing.Color.Transparent);
            graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;

            var bounds = new Drawing.RectangleF(1.5f, 7f, IconSize - 3f, IconSize - 14f);
            using var path = CapsulePath(bounds);
            using var fill = new Drawing.SolidBrush(Drawing.Color.Black);
            using var outline = new Drawing.Pen(Drawing.Color.White, 1.5f);

            graphics.FillPath(fill, path);
            graphics.DrawPath(outline, path);
        }

        return bitmap.GetHicon();
    }

    private static Drawing2D.GraphicsPath CapsulePath(Drawing.RectangleF rect)
    {
        float radius = rect.Height;
        var path = new Drawing2D.GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius, radius, 90, 180);
        path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 180);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
