using Island.App.ViewModels;

namespace Island.App.Views;

/// <summary>Compact media strip: art, title, artist and an equalizer that animates only while visible and playing.</summary>
public partial class MediaPreviewView : System.Windows.Controls.UserControl
{
    private IslandViewModel? _vm;

    public MediaPreviewView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => SyncAnimation();
    }

    /// <summary>Connects the play state so the equalizer follows playback.</summary>
    public void Attach(IslandViewModel vm)
    {
        _vm = vm;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IslandViewModel.IsPlaying)) SyncAnimation();
        };
        SyncAnimation();
    }

    private void SyncAnimation()
    {
        Glyph.SetAnimating(IsVisible && _vm is { IsPlaying: true });
    }
}
