using Island.App.ViewModels;

namespace Island.App.Views;

/// <summary>
/// Now Playing widget (400 x 152): art, title and artist, equalizer, draggable progress with elapsed and remaining time,
/// and the transport row. Shows a placeholder when no media session exists. Bound to the island view model through
/// <see cref="Attach"/>; the widget does not depend on the shell mode.
/// </summary>
public partial class MediaExpandedView : System.Windows.Controls.UserControl
{
    private IslandViewModel? _vm;

    public MediaExpandedView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => SyncAnimation();
    }

    /// <summary>Binds the widget to the view model and wires seeking and interaction state.</summary>
    public void Attach(IslandViewModel vm)
    {
        if (_vm is not null) return;

        _vm = vm;
        DataContext = vm;

        SeekBar.Seeked += fraction => vm.SeekCommand.Execute(fraction);
        SeekBar.DraggingChanged += dragging => vm.SetInteracting(dragging);

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
