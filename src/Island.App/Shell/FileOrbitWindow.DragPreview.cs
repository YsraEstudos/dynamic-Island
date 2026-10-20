using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Island.Core.Files;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;

namespace Island.App.Shell;

public partial class FileOrbitWindow
{
    private DispatcherTimer? _dragReleaseTimer;
    private long? _releasedAt;
    public bool IsDragPreview { get; private set; }

    public void BeginDragPreview()
    {
        IsDragPreview = true;
        ShowActivated = false;
        // While petals move across the pointer, a drop must only finish the batch capture.
        // Settled petals handle their own drop before this bubbling fallback.
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            if (!IsDragPreview || !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            e.Effects = System.Windows.DragDropEffects.Copy; e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (!IsDragPreview || !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            e.Handled = true; CompleteDrop();
        };
        _dragReleaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _dragReleaseTimer.Tick += (_, _) =>
        {
            if (!IsDragPreview) { _dragReleaseTimer.Stop(); return; }
            if ((GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0)
                _releasedAt = null;
            else
            {
                _releasedAt ??= Environment.TickCount64;
                // Give the OLE Drop event time to arrive before dismissing a cancelled/external drop.
                if (Environment.TickCount64 - _releasedAt >= 240) Close();
            }
        };
        _dragReleaseTimer.Start();
        Closed += (_, _) => _dragReleaseTimer.Stop();
    }

    public void CompleteDrop()
    {
        IsDragPreview = false;
        _dragReleaseTimer?.Stop();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (IsVisible) Activate(); }));
    }

    private void AttachActionDrop(Button button, FileAction? action)
    {
        button.AllowDrop = true;
        button.DragEnter += (_, e) =>
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            button.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(49, 75, 59));
            button.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(166, 244, 197));
            Hover(button, true);
        };
        button.DragLeave += (_, _) =>
        {
            button.ClearValue(Control.BackgroundProperty); button.ClearValue(Control.BorderBrushProperty);
            Hover(button, false);
        };
        button.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            e.Effects = System.Windows.DragDropEffects.Copy; e.Handled = true;
        };
        button.Drop += (_, e) =>
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) || !IsDragPreview) return;
            e.Handled = true; CompleteDrop();
            if (action is { } chosen) PickAction(button, chosen);
            else { StoreRequested?.Invoke(_paths); Close(); }
        };
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
