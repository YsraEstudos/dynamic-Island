using System.IO;
using System.Windows;
using System.Windows.Media;
using DragEventArgs = System.Windows.DragEventArgs;

namespace Island.App.Shell;

public partial class IslandWindow
{
    private FileOrbitWindow? _fileOrbit;

    private void AttachFileDrop()
    {
        IslandShape.AllowDrop = true;
        IslandShape.PreviewDragEnter += OnFileDragOver;
        IslandShape.PreviewDragOver += OnFileDragOver;
        IslandShape.PreviewDragLeave += (_, _) => ClearFileDrag();
        IslandShape.PreviewDrop += OnFilesDropped;
        Closed += (_, _) => _fileOrbit?.Close();
    }

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        e.Handled = true;
        e.Effects = _vm.Suspended ? System.Windows.DragDropEffects.None : System.Windows.DragDropEffects.Copy;
        if (_vm.Suspended) return;
        IslandShape.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(166, 244, 197));
        IslandShape.BorderThickness = new Thickness(2);
        _dragActive = true;
        UpdateInteraction(_vm.Mode);
        if (_fileOrbit is null && e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
            OpenFileOrbit(paths, PointToScreen(e.GetPosition(this)), preview: true);
    }

    private void ClearFileDrag()
    {
        IslandShape.BorderThickness = new Thickness(0);
        _dragActive = false;
        UpdateInteraction(_vm.Mode);
    }

    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        ClearFileDrag(); e.Handled = true;
        if (_vm.Suspended || e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths) return;
        if (_fileOrbit is { IsDragPreview: true }) { _fileOrbit.CompleteDrop(); return; }
        OpenFileOrbit(paths, PointToScreen(e.GetPosition(this)), preview: false);
    }

    private void OpenFileOrbit(string[] paths, System.Windows.Point anchor, bool preview)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (existing.Length == 0) return;
        if (_fileOrbit is { IsRunning: true }) { _fileOrbit.Activate(); return; }
        _fileOrbit?.Close();
        var orbit = new FileOrbitWindow(existing, _settings().ReduceAnimations) { Owner = this };
        orbit.StoreRequested += files => _vm.Shelf.FileTray.Add(files);
        _fileOrbit = orbit;
        orbit.Closed += (_, _) => { if (ReferenceEquals(_fileOrbit, orbit)) _fileOrbit = null; };
        // Work in monitor coordinates, then convert physical pixels to WPF DIPs.
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)anchor.X, (int)anchor.Y)).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new System.Windows.Point(area.Left, area.Top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(area.Right, area.Bottom));
        var point = fromDevice.Transform(anchor);
        orbit.PlaceAt(point, new Rect(topLeft, bottomRight));
        if (preview) orbit.BeginDragPreview();
        orbit.Show();
    }
}
