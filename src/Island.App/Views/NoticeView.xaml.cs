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

    public NoticeView()
    {
        InitializeComponent();
    }

    /// <summary>Follows the notice so the glyph updates when a new notice arrives while this view is shown.</summary>
    public void Attach(IslandViewModel vm)
    {
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IslandViewModel.Notice)) ShowGlyph(vm.Notice);
        };
        ShowGlyph(vm.Notice);
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
