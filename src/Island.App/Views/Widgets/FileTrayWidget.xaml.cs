using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Island.App.Widgets;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Island.App.Views.Widgets;

/// <summary>
/// File tray widget (280 x 152). Files dropped on it are added to the shelf tray; each file is a chip that can be dragged
/// out to other applications and removed with its close button. Tray changes arrive on arbitrary threads and are
/// coalesced onto the UI thread.
/// </summary>
public partial class FileTrayWidget : UserControl
{
    private static readonly Brush SurfaceFill = Frozen(0x16, 0x16, 0x16);
    private static readonly Brush SurfaceHover = Frozen(0x1C, 0x1C, 0x1E);
    private static readonly Brush IdleStroke = Frozen(0x48, 0x48, 0x4A);
    private static readonly Brush HoverStroke = Brushes.White;
    private static readonly Brush ChipFill = Frozen(0x2C, 0x2C, 0x2E);
    private static readonly Brush ChipButtonFill = Frozen(0x3A, 0x3A, 0x3C);
    private static readonly Brush Primary = Brushes.White;
    private static readonly Brush Secondary = Frozen(0xA1, 0xA1, 0xA6);

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".svg", ".tif", ".tiff",
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz",
    };

    private readonly ShelfContext _ctx;
    private readonly UiSignal _signal;
    private bool _subscribed;

    public FileTrayWidget(ShelfContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        _signal = new UiSignal(Dispatcher, Refresh);

        ClearButton.Click += () => _ctx.FileTray.Clear();

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => SetDragHighlight(false);
        Drop += OnDrop;

        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed) return;

        _ctx.FileTray.Changed += OnTrayChanged;
        _subscribed = true;
        Refresh();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        _ctx.FileTray.Changed -= OnTrayChanged;
        _subscribed = false;
    }

    /// <summary>Arbitrary thread.</summary>
    private void OnTrayChanged() => _signal.Signal();

    private void Refresh()
    {
        IReadOnlyList<string> paths = _ctx.FileTray.Paths;
        bool any = paths.Count > 0;

        EmptyPanel.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        FilesPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = paths.Count == 1 ? "1 file" : $"{paths.Count} files";

        Chips.Children.Clear();
        foreach (string path in paths)
        {
            Chips.Children.Add(BuildChip(path));
        }
    }

    private Border BuildChip(string path)
    {
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        bool isFolder = string.IsNullOrEmpty(extension) && Directory.Exists(path);
        string iconKey = isFolder ? "WidgetIcon.Folder"
            : ImageExtensions.Contains(extension) ? "WidgetIcon.Image"
            : ArchiveExtensions.Contains(extension) ? "WidgetIcon.Archive"
            : "WidgetIcon.Document";

        string displayName = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(displayName)) displayName = path;

        var name = new TextBlock
        {
            Text = displayName,
            FontSize = 12,
            Foreground = Primary,
            MaxWidth = 150,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 6, 0),
        };
        name.SetResourceReference(TextBlock.FontFamilyProperty, "IslandFontFamily");

        var remove = new ShelfButton
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = ChipButtonFill,
            Icon = WidgetGlyph.Find("WidgetIcon.Close"),
            IconSize = 10,
            IconBrush = Secondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        remove.Click += () => _ctx.FileTray.Remove(path);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(WidgetGlyph.Create(WidgetGlyph.Find(iconKey) ?? Geometry.Empty, Secondary, 16));
        row.Children.Add(name);
        row.Children.Add(remove);

        var chip = new Border
        {
            Height = 34,
            CornerRadius = new CornerRadius(12),
            Background = ChipFill,
            Padding = new Thickness(9, 0, 4, 0),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = row,
        };

        // Drag the chip out to another application as a file drop.
        Point? pressed = null;
        chip.MouseLeftButtonDown += (_, e) => pressed = e.GetPosition(chip);
        chip.MouseLeftButtonUp += (_, _) => pressed = null;
        chip.MouseMove += (_, e) =>
        {
            if (pressed is not { } start || e.LeftButton != MouseButtonState.Pressed) return;

            Point now = e.GetPosition(chip);
            bool moved = Math.Abs(now.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance
                         || Math.Abs(now.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
            if (!moved) return;

            pressed = null;
            var data = new System.Windows.DataObject(DataFormats.FileDrop, new[] { path });
            System.Windows.DragDrop.DoDragDrop(chip, data, DragDropEffects.Copy | DragDropEffects.Move);
        };

        return chip;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool accepts = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        SetDragHighlight(accepts);
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        SetDragHighlight(false);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            _ctx.FileTray.Add(paths);
        }
        e.Handled = true;
    }

    private void SetDragHighlight(bool on)
    {
        DropFrame.Stroke = on ? HoverStroke : IdleStroke;
        Surface.Background = on ? SurfaceHover : SurfaceFill;
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
