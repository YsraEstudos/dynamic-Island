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
    private bool _subscribed;

    public MediaExpandedView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => SyncAnimation();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => { if (_vm is not null) _vm.PropertyChanged -= OnMediaChanged; _subscribed = false; Glyph.SetAnimating(false); };
    }

    /// <summary>Binds the widget to the view model and wires seeking and interaction state.</summary>
    public void Attach(IslandViewModel vm)
    {
        if (_vm is not null) return;

        _vm = vm;
        DataContext = vm;

        SeekBar.Seeked += fraction => vm.SeekCommand.Execute(fraction);
        SeekBar.DraggingChanged += dragging => vm.SetInteracting(dragging);

        Subscribe();
        SyncAnimation();
    }

    private void Subscribe()
    {
        if (_vm is null || _subscribed) return;
        _vm.PropertyChanged += OnMediaChanged;
        _subscribed = true;
        SyncAnimation();
    }

    private void OnMediaChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IslandViewModel.IsPlaying)) SyncAnimation();
    }

    private void SyncAnimation()
    {
        Glyph.SetAnimating(IsVisible && _vm is { IsPlaying: true });
    }
}
