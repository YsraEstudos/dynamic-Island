using System.Windows;
using System.Windows.Media;
using Island.App.ViewModels;
using Island.Core.Models;
using Brush = System.Windows.Media.Brush;

namespace Island.App.Views;

/// <summary>
/// Temporary toast (320 x 56): optional glyph, title and optional subtitle. Title and subtitle bind to the
/// view model's <see cref="IslandViewModel.Notice"/>; the glyph is chosen in code from the notice's key.
/// </summary>
public partial class NoticeView : System.Windows.Controls.UserControl
{
    private const string CheckGlyph = "check";
    private const string TimerGlyph = "timer";

    public static readonly System.Windows.DependencyProperty DisplayedNoticeProperty =
        System.Windows.DependencyProperty.Register(nameof(DisplayedNotice), typeof(Notice), typeof(NoticeView));

    public Notice? DisplayedNotice
    {
        get => (Notice?)GetValue(DisplayedNoticeProperty);
        private set => SetValue(DisplayedNoticeProperty, value);
    }

    public NoticeView()
    {
        InitializeComponent();
    }

    /// <summary>Seeds the snapshot used when the layer is first shown.</summary>
    public void Attach(IslandViewModel vm)
    {
        SetNotice(vm.Notice);
    }

    /// <summary>
    /// Updates the displayed snapshot. IslandWindow calls this from the transition hook, after the old toast has
    /// faded out, so a consecutive notice never changes its text underneath the outgoing animation.
    /// </summary>
    public void SetNotice(Notice? notice)
    {
        DisplayedNotice = notice;
        ShowGlyph(notice);
    }

    private void ShowGlyph(Notice? notice)
    {
        switch (notice?.Glyph)
        {
            case CheckGlyph:
                SetGlyph("UiIconCheck", "AccentGreenBrush");
                break;
            case TimerGlyph:
                SetGlyph("UiIconTimer", "AccentOrangeBrush");
                break;
            case "caps-on":
                SetGlyph("NoticeIconCapsOn", "AccentGreenBrush");
                break;
            case "caps-off":
                SetGlyph("NoticeIconCapsOff", "TextSecondaryBrush");
                break;
            case "bluetooth-on":
                SetGlyph("NoticeIconBluetoothOn", "AccentBlueBrush");
                break;
            case "bluetooth-off":
                SetGlyph("NoticeIconBluetoothOff", "TextSecondaryBrush");
                break;
            case "usb-on":
                SetGlyph("NoticeIconUsbOn", "AccentBlueBrush");
                break;
            case "usb-off":
                SetGlyph("NoticeIconUsbOff", "TextSecondaryBrush");
                break;
            default:
                GlyphBox.Visibility = System.Windows.Visibility.Collapsed;
                GapColumn.Width = new GridLength(0);
                break;
        }
    }

    private void SetGlyph(string geometryKey, string brushKey)
    {
        GlyphPath.Data = (Geometry)FindResource(geometryKey);
        GlyphPath.Stroke = (Brush)FindResource(brushKey);
        GlyphBox.Visibility = System.Windows.Visibility.Visible;
        GapColumn.Width = new GridLength(12);
    }
}
