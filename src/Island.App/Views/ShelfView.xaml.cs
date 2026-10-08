using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Island.App.Animations;
using Island.App.ViewModels;
using Island.App.Widgets;
using Island.Core.Configuration;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Island.App.Views;

/// <summary>
/// Hosts the shelf. The shelf is a list of rows (pages) and only the row on screen is shown; the island is as wide as
/// that row. Expanded shows the saved row. Customize (the same view, switched by <see cref="SetPresentation"/>) adds
/// gestures on top of the row: reorder a card along the row, add a widget by dragging a tray tile into the row,
/// remove a card by dragging it down into the tray, and move a card to the row above (drop at the top edge) or the
/// row below (drop in the strip under the band). A plain click on a tile still toggles it. Cards are placed by springs
/// on a canvas, so every change animates. Changing the row slides the canvas.
/// </summary>
public partial class ShelfView : System.Windows.Controls.UserControl, IFrameClient
{
    private const double DragThreshold = 5.0;
    /// <summary>Pointer Y above which a dragged card moves to the row above (the island's top edge).</summary>
    private const double RowAboveY = 12.0;
    /// <summary>Pointer Y at and below which a dragged card is in the tray (removal zone). The strip between the band and this line is the row below.</summary>
    private const double RowZoneBottom = IslandShapeTable.ShelfHeight + 40.0;
    private const double ReduceFadeMilliseconds = 120.0;
    private const double AppearScale = 0.85;
    private const double GhostRestScale = 0.6;
    private const double ExitScale = 0.6;
    private const double LiftScale = 1.03;
    private const double LiftOpacity = 0.92;
    private const double RemovingScale = 0.85;
    private const double RemovingOpacity = 0.5;
    private const double GhostOpacity = 0.9;
    private const double BlockedGhostOpacity = 0.4;
    private const int LiftZIndex = 10;
    private const int GhostZIndex = 30;
    private const double PageSlideOffset = 28.0;
    private static readonly TimeSpan PageOutDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan PageInDuration = TimeSpan.FromMilliseconds(180);

    private IslandViewModel? _vm;
    private readonly Dictionary<string, ShelfCard> _cards = new(StringComparer.Ordinal);
    private readonly List<ShelfCard> _all = new();                 // every card, stepped by the frame pump
    private readonly Dictionary<string, double> _slotX = new(StringComparer.Ordinal);
    private readonly TranslateTransform _pageOffset = new();       // slides the cards when the row changes
    private List<List<string>> _savedRows = new() { new List<string>() };    // rows as saved; never empty
    private List<List<string>> _workingRows = new() { new List<string>() };  // rows being edited; never empty
    private int _page;                                             // index of the row on screen
    private List<string>? _preview;                                // order of the row on screen during a drag, else null
    private bool _editing;
    private bool _sliding;
    private int _slideToken;
    private Press? _press;
    private Drag? _drag;

    public ShelfView()
    {
        InitializeComponent();
        WidgetCanvas.RenderTransform = _pageOffset;
        CancelButton.MouseLeftButtonUp += OnCancelClicked;
        CommitButton.MouseLeftButtonUp += OnCommitClicked;
        MouseMove += OnPointerMove;
        MouseLeftButtonUp += OnPointerUp;
        LostMouseCapture += OnCaptureLost;
    }

    /// <summary>Raised when the rows, the drag preview or the row on screen change, so the island can resize to them.</summary>
    public event Action? LayoutChanged;

    /// <summary>Raised when a drag starts (true) or ends (false). The island holds itself open meanwhile.</summary>
    public event Action<bool>? DragActiveChanged;

    private bool Reduce => _vm?.Settings.ReduceAnimations ?? false;

    /// <summary>Index of the row on screen.</summary>
    public int Page => _page;

    /// <summary>Number of rows of the saved list, or of the working copy when <paramref name="editing"/>.</summary>
    public int PageCount(bool editing) => (editing ? _workingRows : _savedRows).Count;

    /// <summary>Widgets of the row on screen: the saved row, or the working row with its drag preview when editing.</summary>
    public IReadOnlyList<string> PageIds(bool editing) => editing ? _preview ?? WorkingRow : RowAt(_savedRows);

    public void Attach(IslandViewModel vm)
    {
        _vm = vm;
        _savedRows = IslandShapeTable.FitRows(vm.Settings.GetShelfRows());
    }

    /// <summary>Starts an edit session on a copy of the saved rows, fitted to the width limit. Shows nothing by itself.</summary>
    public void BeginEdit(IReadOnlyList<IReadOnlyList<string>> saved)
    {
        CancelDrag();
        CancelSlide();
        _workingRows = IslandShapeTable.FitRows(saved);
        _page = ShelfLayout.ValidPage(_page, _workingRows.Count);
    }

    /// <summary>Switches between the saved presentation and the edit presentation. Call while the content is hidden.</summary>
    public void SetPresentation(bool editing, IReadOnlyList<IReadOnlyList<string>> saved)
    {
        CancelDrag();
        CancelSlide();
        _editing = editing;
        _savedRows = IslandShapeTable.FitRows(saved);
        _page = ShelfLayout.ValidPage(_page, Rows.Count);
        Relayout(instant: true);
        TrayRoot.Visibility = editing ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (editing) RebuildTray();
    }

    /// <summary>The saved rows changed (settings applied). Re-lays out only when it actually differs.</summary>
    public void SetSaved(IReadOnlyList<IReadOnlyList<string>> saved)
    {
        List<List<string>> fitted = IslandShapeTable.FitRows(saved);
        if (SameRows(_savedRows, fitted)) return;

        _savedRows = fitted;
        if (!_editing)
        {
            _page = ShelfLayout.ValidPage(_page, _savedRows.Count);
            Relayout(instant: false);
        }
    }

    /// <summary>Moves to the neighbouring row with a slide. False at the first or last row, or while a drag or slide runs.</summary>
    public bool TryMovePage(int delta)
    {
        if (_vm is null || _sliding || _drag is not null || _press is not null) return false;

        int target = _page + delta;
        if (target < 0 || target >= Rows.Count) return false;

        SlidePage(delta, () => _page = target);
        return true;
    }

    /// <summary>Shows a row at once, without a slide. Used when the wheel moves from the Clipboard back to the last row.</summary>
    public void JumpToPage(int page)
    {
        CancelSlide();
        _page = Math.Clamp(page, 0, Rows.Count - 1);
        Relayout(instant: true);
    }

    // ---- Rows -----------------------------------------------------------------------------------------------

    /// <summary>The rows on screen: the working copy while editing, the saved rows otherwise.</summary>
    private List<List<string>> Rows => _editing ? _workingRows : _savedRows;

    private List<string> WorkingRow => RowAt(_workingRows);

    private List<string> RowAt(List<List<string>> rows) => rows[Math.Clamp(_page, 0, rows.Count - 1)];

    /// <summary>The order the cards are laid out in: the row on screen (with its drag preview while editing).</summary>
    private IReadOnlyList<string> Sequence() => _editing ? _preview ?? WorkingRow : RowAt(_savedRows);

    /// <summary>Drops empty rows (one empty row stays when all are empty) and points the page at <paramref name="keep"/>.</summary>
    private void PruneRows(List<string> keep)
    {
        _workingRows.RemoveAll(row => row.Count == 0);
        if (_workingRows.Count == 0) _workingRows.Add(new List<string>());

        int index = _workingRows.IndexOf(keep);
        _page = index >= 0 ? index : Math.Clamp(_page, 0, _workingRows.Count - 1);
    }

    /// <summary>Adds an empty row above (direction -1) or below (direction 1) the working rows.</summary>
    private List<string> CreateRow(int direction)
    {
        var row = new List<string>();
        if (direction < 0) _workingRows.Insert(0, row);
        else _workingRows.Add(row);
        return row;
    }

    private static bool SameRows(List<List<string>> a, List<List<string>> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => pair.First.SequenceEqual(pair.Second));

    // ---- Layout ----------------------------------------------------------------------------------------------

    /// <summary>Width the island has (or will have) for these widgets; Customize keeps its minimum.</summary>
    private double TargetWidth(IEnumerable<string> ids)
    {
        double width = IslandShapeTable.ShelfWidth(ids);
        return _editing ? Math.Max(IslandShapeTable.CustomizeMinWidth, width) : width;
    }

    /// <summary>Left edges of consecutive cards, centred in <paramref name="targetWidth"/> with 12 DIP gaps.</summary>
    private static double[] Lefts(IReadOnlyList<WidgetDescriptor> seq, double targetWidth)
    {
        double inner = 0.0;
        foreach (WidgetDescriptor descriptor in seq) inner += descriptor.Width;
        if (seq.Count > 1) inner += IslandShapeTable.ShelfGap * (seq.Count - 1);

        var lefts = new double[seq.Count];
        double x = (targetWidth - inner) / 2.0;
        for (int i = 0; i < seq.Count; i++)
        {
            lefts[i] = x;
            x += seq[i].Width + IslandShapeTable.ShelfGap;
        }
        return lefts;
    }

    /// <summary>Insert index for a widget centred at <paramref name="centreX"/> among <paramref name="ids"/>, judged on the base layout of <paramref name="layout"/>.</summary>
    private int IndexAt(IEnumerable<string> ids, IReadOnlyList<string> layout, double centreX)
    {
        IReadOnlyList<WidgetDescriptor> seq = IslandShapeTable.Resolve(ids);
        double[] lefts = Lefts(seq, TargetWidth(layout));
        int index = 0;
        while (index < seq.Count && lefts[index] + seq[index].Width / 2.0 < centreX) index++;
        return index;
    }

    /// <summary>Places every card of the row on screen (springing to its slot) and takes off cards that left it.</summary>
    private void Relayout(bool instant)
    {
        if (_vm is null) return;

        bool snap = instant || Reduce;
        IReadOnlyList<WidgetDescriptor> seq = IslandShapeTable.Resolve(Sequence());
        double[] lefts = Lefts(seq, TargetWidth(seq.Select(d => d.Id)));
        _slotX.Clear();
        var placed = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < seq.Count; i++)
        {
            WidgetDescriptor descriptor = seq[i];
            _slotX[descriptor.Id] = lefts[i];
            placed.Add(descriptor.Id);
            if (IsDragged(descriptor.Id)) continue;   // a dragged card follows the pointer; a gap ghost has no card yet

            ShelfCard card = GetCard(descriptor);
            if (card.Mode == CardMode.Leaving)
            {
                // Re-added before it finished leaving: keep the card.
                card.Mode = CardMode.Normal;
                card.Completion = null;
            }
            if (!IsOnCanvas(card))
            {
                // A card entering the row appears in place, small and transparent, then springs to full size.
                WidgetCanvas.Children.Add(card.Root);
                card.Pose(lefts[i], 0.0, snap ? 1.0 : AppearScale, snap ? 1.0 : 0.0, instant: true);
            }
            card.SetEditing(_editing);
            card.Pose(lefts[i], 0.0, 1.0, 1.0, snap);
        }

        foreach (ShelfCard card in _cards.Values)
        {
            if (card.Mode == CardMode.Normal && !placed.Contains(card.Id) && IsOnCanvas(card))
            {
                WidgetCanvas.Children.Remove(card.Root);
            }
        }

        UpdatePageDots();
        MotionPump.Request(this);
    }

    /// <summary>One dot per row, the row on screen bright. Hidden when there is a single row.</summary>
    private void UpdatePageDots()
    {
        int count = Rows.Count;
        PageDots.Visibility = count > 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (count <= 1) return;

        while (PageDots.Children.Count < count)
        {
            PageDots.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 5,
                Height = 5,
                Margin = new Thickness(3, 0, 3, 0),
                Fill = Brushes.White,
            });
        }
        while (PageDots.Children.Count > count) PageDots.Children.RemoveAt(PageDots.Children.Count - 1);

        for (int i = 0; i < count; i++)
        {
            PageDots.Children[i].Opacity = i == _page ? 0.95 : 0.35;
        }
    }

    private bool IsOnCanvas(ShelfCard card) => ReferenceEquals(card.Root.Parent, WidgetCanvas);

    private bool IsDragged(string id) => _drag is not null && _drag.Id == id;

    /// <summary>Card for a widget, created once and kept so its state survives list changes.</summary>
    private ShelfCard GetCard(WidgetDescriptor descriptor)
    {
        if (_cards.TryGetValue(descriptor.Id, out ShelfCard? existing)) return existing;

        IslandViewModel vm = _vm!;
        FrameworkElement content = descriptor.Create(vm.Shelf, vm);
        var card = new ShelfCard(descriptor, content, removable: true);
        string id = descriptor.Id;
        card.RemoveRequested = () => RemoveFromWorking(id);
        card.Root.MouseLeftButtonDown += (_, e) => OnCardPressed(card, e);

        _cards[id] = card;
        _all.Add(card);
        return card;
    }

    private void RemoveFromWorking(string id)
    {
        List<string>? row = _workingRows.Find(r => r.Contains(id));
        if (row is null) return;

        List<string> page = WorkingRow;
        if (ReferenceEquals(row, page) && _cards.TryGetValue(id, out ShelfCard? card)
            && IsOnCanvas(card) && card.Mode == CardMode.Normal)
        {
            ExitCard(card);
        }
        row.Remove(id);
        PruneRows(page);
        Relayout(instant: false);
        RebuildTray();
        LayoutChanged?.Invoke();
    }

    /// <summary>Fades a removed card out (a spring, or a 120 ms fade with reduced motion) and then takes it off the canvas.</summary>
    private void ExitCard(ShelfCard card)
    {
        card.Mode = CardMode.Leaving;
        card.Completion = null;

        if (Reduce)
        {
            var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(ReduceFadeMilliseconds))
            {
                FillBehavior = FillBehavior.HoldEnd,
            };
            fade.Completed += (_, _) =>
            {
                card.Root.BeginAnimation(UIElement.OpacityProperty, null);
                Detach(card);
            };
            card.Root.BeginAnimation(UIElement.OpacityProperty, fade);
            return;
        }

        card.Pose(card.X.Target, card.Y.Target, ExitScale, 0.0, instant: false);
        card.Completion = () => Detach(card);
        MotionPump.Request(this);
    }

    private void Detach(ShelfCard card)
    {
        if (IsOnCanvas(card)) WidgetCanvas.Children.Remove(card.Root);
        card.Mode = CardMode.Normal;
    }

    private void RemoveGhost(ShelfCard ghost)
    {
        if (IsOnCanvas(ghost)) WidgetCanvas.Children.Remove(ghost.Root);
        _all.Remove(ghost);
    }

    private void Lift(ShelfCard card, double scale, double opacity) =>
        card.Pose(card.X.Target, card.Y.Target, scale, opacity, Reduce);

    private static void RunCompletion(ShelfCard card)
    {
        Action? action = card.Completion;
        card.Completion = null;
        action?.Invoke();
    }

    // ---- Row slide ------------------------------------------------------------------------------------------

    /// <summary>
    /// Changes the row on screen: the canvas slides out and fades (up for a row below, down for one above), the
    /// row changes while it is hidden, then the new row slides in from the other side. Reduced motion just switches.
    /// </summary>
    private void SlidePage(int direction, Action change)
    {
        if (Reduce)
        {
            change();
            Relayout(instant: true);
            LayoutChanged?.Invoke();
            return;
        }

        int token = ++_slideToken;
        _sliding = true;
        AnimatePage(0.0, -direction * PageSlideOffset, 1.0, 0.0, PageOutDuration,
            new QuarticEase { EasingMode = EasingMode.EaseIn }, () =>
            {
                if (token != _slideToken) return;

                change();
                Relayout(instant: true);
                LayoutChanged?.Invoke();
                AnimatePage(direction * PageSlideOffset, 0.0, 0.0, 1.0, PageInDuration,
                    new CubicEase { EasingMode = EasingMode.EaseOut }, () =>
                    {
                        if (token == _slideToken) _sliding = false;
                    });
            });
    }

    /// <summary>Animates the canvas offset and opacity; <paramref name="done"/> runs when the slide settles.</summary>
    private void AnimatePage(double fromY, double toY, double fromOpacity, double toOpacity,
        TimeSpan duration, IEasingFunction easing, Action done)
    {
        var slide = new DoubleAnimation(fromY, toY, duration) { EasingFunction = easing };
        var fade = new DoubleAnimation(fromOpacity, toOpacity, duration) { EasingFunction = easing };
        slide.Completed += (_, _) =>
        {
            _pageOffset.Y = toY;
            WidgetCanvas.Opacity = toOpacity;
            _pageOffset.BeginAnimation(TranslateTransform.YProperty, null);
            WidgetCanvas.BeginAnimation(UIElement.OpacityProperty, null);
            done();
        };
        _pageOffset.BeginAnimation(TranslateTransform.YProperty, slide);
        WidgetCanvas.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Stops a running slide at once and resets the canvas.</summary>
    private void CancelSlide()
    {
        _slideToken++;
        _sliding = false;
        _pageOffset.BeginAnimation(TranslateTransform.YProperty, null);
        WidgetCanvas.BeginAnimation(UIElement.OpacityProperty, null);
        _pageOffset.Y = 0.0;
        WidgetCanvas.Opacity = 1.0;
    }

    // ---- Tray -----------------------------------------------------------------------------------------------

    private void RebuildTray()
    {
        TileRow.Children.Clear();
        foreach (WidgetDescriptor descriptor in WidgetCatalog.All)
        {
            bool onShelf = _workingRows.Any(row => row.Contains(descriptor.Id));
            // Not on the shelf and adding it would exceed the maximum width of the row on screen: shown disabled.
            bool blocked = !onShelf && !IslandShapeTable.Fits(WorkingRow, descriptor.Id);
            TileRow.Children.Add(BuildTile(descriptor, onShelf, blocked));
        }
    }

    /// <summary>
    /// 44 x 44 icon tile with the title below. A widget already on the shelf is dimmed and gets a check;
    /// a blocked one is dimmed and gets no check. Pressing a tile that is not on the shelf starts an add drag.
    /// </summary>
    private UIElement BuildTile(WidgetDescriptor descriptor, bool onShelf, bool blocked)
    {
        double dim = onShelf || blocked ? 0.4 : 1.0;

        var body = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(12),
            Background = Resource<Brush>("ShelfTileBrush"),
            Opacity = dim,
        };
        Geometry? icon = WidgetGlyph.Find(descriptor.IconKey);
        if (icon is not null)
        {
            Viewbox glyph = WidgetGlyph.Create(icon, Brushes.White, 22);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            body.Child = glyph;
        }

        var tile = new Grid
        {
            Width = 44,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        tile.Children.Add(body);
        if (onShelf) tile.Children.Add(CreateCheckBadge());

        var caption = new TextBlock
        {
            Text = descriptor.Title,
            Width = 72,
            Margin = new Thickness(0, 4, 0, 0),
            Opacity = dim,
            Style = Resource<Style>("TileCaptionTextStyle"),
        };

        var column = new StackPanel { Width = 72 };
        column.Children.Add(tile);
        column.Children.Add(caption);

        var button = new Border
        {
            Background = Brushes.Transparent,
            Cursor = blocked ? Cursors.No : Cursors.Hand,
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = column,
        };
        button.MouseLeftButtonDown += (_, e) => OnTilePressed(descriptor, button, e);
        return button;
    }

    private static Border CreateCheckBadge()
    {
        Geometry? check = WidgetGlyph.Find("UiIconCheck");
        var badge = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = Resource<Brush>("AccentBlueBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -5, -5, 0),
        };
        if (check is not null)
        {
            badge.Child = CreateStrokeGlyph(check, Brushes.White, 10.0, 2.8);
        }
        return badge;
    }

    private static Viewbox CreateStrokeGlyph(Geometry geometry, Brush brush, double size, double strokeThickness)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = strokeThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = canvas,
        };
    }

    /// <summary>Toggles a tile on a click: removes the widget from its row, or adds it to the end of the row on screen.</summary>
    private void ToggleTile(string id)
    {
        if (!_editing) return;

        if (_workingRows.Any(row => row.Contains(id)))
        {
            RemoveFromWorking(id);
            return;
        }
        if (!IslandShapeTable.Fits(WorkingRow, id)) return;

        WorkingRow.Add(id);
        Relayout(instant: false);
        RebuildTray();
        LayoutChanged?.Invoke();
    }

    // ---- Pointer gestures -----------------------------------------------------------------------------------

    private void OnCardPressed(ShelfCard card, MouseButtonEventArgs e)
    {
        if (!_editing || _drag is not null || _sliding) return;

        _press = new Press(card.Root, e.GetPosition(this), card: card);
        card.Root.CaptureMouse();
    }

    private void OnTilePressed(WidgetDescriptor descriptor, FrameworkElement tile, MouseButtonEventArgs e)
    {
        if (!_editing || _drag is not null || _sliding) return;

        bool addable = !_workingRows.Any(row => row.Contains(descriptor.Id));
        _press = new Press(tile, e.GetPosition(this), tile: descriptor, addable: addable);
        tile.CaptureMouse();
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (_press is null && _drag is null) return;

        Point p = e.GetPosition(this);
        if (_drag is null)
        {
            if (_press is null || (p - _press.Start).Length <= DragThreshold) return;
            if (!BeginDrag()) return;
        }
        UpdateDrag(p);
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not null)
        {
            UpdateDrag(e.GetPosition(this));
            FinishDrag();
            return;
        }

        if (_press is null) return;
        Press press = _press;
        _press = null;
        ReleaseCapture(press.Source);

        if (press.TileDescriptor is { } descriptor) ToggleTile(descriptor.Id);
    }

    /// <summary>Capture was lost (window deactivated, etc.): end the gesture where it stands.</summary>
    private void OnCaptureLost(object sender, RoutedEventArgs e)
    {
        if (_drag is not null) FinishDrag();
        else _press = null;
    }

    /// <summary>Starts the drag once the pointer has moved past the threshold. Returns false when the press cannot drag.</summary>
    private bool BeginDrag()
    {
        Press press = _press!;

        if (press.Card is { } card)
        {
            _drag = new Drag(isAdd: false, card.Id, card, press.Source)
            {
                GrabX = press.Start.X - card.X.Target,
                PressY = press.Start.Y,
            };
            card.Mode = CardMode.Dragging;
            card.Tune(follow: true);
            Panel.SetZIndex(card.Root, LiftZIndex);
            Lift(card, LiftScale, LiftOpacity);
            BeginDragging(Cursors.SizeAll);
            return true;
        }

        if (press.TileDescriptor is { } descriptor && press.Addable)
        {
            Point centre = press.Source.TransformToAncestor(this)
                .Transform(new Point(press.Source.ActualWidth / 2.0, press.Source.ActualHeight / 2.0));
            ShelfCard ghost = CreateGhost(descriptor);
            ghost.Tune(follow: true);
            ghost.Pose(centre.X - ghost.Width / 2.0,
                centre.Y - WidgetCatalog.WidgetHeight / 2.0 - ShelfCard.RestTop,
                GhostRestScale, GhostOpacity, instant: true);
            WidgetCanvas.Children.Add(ghost.Root);
            Panel.SetZIndex(ghost.Root, GhostZIndex);
            _all.Add(ghost);

            _drag = new Drag(isAdd: true, descriptor.Id, ghost, press.Source) { TileCentre = centre };
            BeginDragging(Cursors.Hand);
            return true;
        }

        return false;
    }

    private void BeginDragging(System.Windows.Input.Cursor cursor)
    {
        Mouse.OverrideCursor = cursor;
        DragActiveChanged?.Invoke(true);
        MotionPump.Request(this);
    }

    private void EndDragging()
    {
        Mouse.OverrideCursor = null;
        DragActiveChanged?.Invoke(false);
    }

    private void UpdateDrag(Point p)
    {
        if (_drag is null) return;

        if (_drag.IsAdd) UpdateAddDrag(p);
        else UpdateReorderDrag(p);
        MotionPump.Request(this);
    }

    /// <summary>Zone of a pointer Y: above the island (row above), the band (this row), the strip under it (row below), or the tray.</summary>
    private static DropZone ZoneOf(double y) =>
        y < RowAboveY ? DropZone.Above
        : y < IslandShapeTable.ShelfHeight ? DropZone.Row
        : y < RowZoneBottom ? DropZone.Below
        : DropZone.Tray;

    /// <summary>
    /// Where a widget lands for a pointer in <paramref name="zone"/>: an index in the row on screen, or in the row
    /// above or below (a new row when there is none). Null when it would be dropped nowhere: the tray for a card,
    /// and the tray or the strip below for a tile.
    /// </summary>
    private Landing? LandingFor(DropZone zone, string id, double centreX, bool isAdd)
    {
        switch (zone)
        {
            case DropZone.Row:
            {
                List<string> row = WorkingRow;
                int index = IndexAt(row.Where(x => x != id), row, centreX);
                return new Landing(row, index, Direction: 0, Blocked: isAdd && !IslandShapeTable.Fits(row, id));
            }
            case DropZone.Above:
                return NeighbourLanding(_page - 1, -1, id, centreX);
            case DropZone.Below when !isAdd:
                return NeighbourLanding(_page + 1, 1, id, centreX);
            default:
                return null;
        }
    }

    private Landing NeighbourLanding(int row, int direction, string id, double centreX)
    {
        if (row < 0 || row >= _workingRows.Count) return new Landing(null, 0, direction, Blocked: false);

        List<string> target = _workingRows[row];
        return new Landing(target, IndexAt(target, target, centreX), direction, Blocked: !IslandShapeTable.Fits(target, id));
    }

    private void UpdateReorderDrag(Point p)
    {
        Drag drag = _drag!;
        ShelfCard card = drag.Card;
        card.Pose(p.X - drag.GrabX, p.Y - drag.PressY, card.Scale.Target, card.Opacity.Target, Reduce);

        DropZone zone = ZoneOf(p.Y);
        bool removing = zone == DropZone.Tray;
        if (removing != (drag.Zone == DropZone.Tray))
        {
            card.Mode = removing ? CardMode.Removing : CardMode.Dragging;
            if (removing) Lift(card, RemovingScale, RemovingOpacity);
            else Lift(card, LiftScale, LiftOpacity);
        }
        drag.Zone = zone;

        double centreX = p.X - drag.GrabX + card.Width / 2.0;
        drag.Target = LandingFor(zone, drag.Id, centreX, isAdd: false);
        drag.Blocked = drag.Target?.Blocked ?? false;

        // The card leaves the row on screen for the drop zones; a drop in the row puts it at its index.
        List<string> preview = WorkingRow.Where(id => id != drag.Id).ToList();
        if (drag.Target is { Direction: 0 } inRow) preview.Insert(inRow.Index, drag.Id);
        SetPreview(preview);
        ShowDropIndicator(zone is DropZone.Above or DropZone.Below ? zone : null, drag.Blocked);
    }

    private void UpdateAddDrag(Point p)
    {
        Drag drag = _drag!;
        ShelfCard ghost = drag.Card;

        DropZone zone = ZoneOf(p.Y);
        bool inside = p.X >= 0.0 && p.X <= ActualWidth;
        drag.Zone = zone;
        drag.Target = inside ? LandingFor(zone, drag.Id, p.X, isAdd: true) : null;
        drag.Blocked = drag.Target?.Blocked ?? false;

        // The ghost shrinks to 0.6 away from the row and grows to full size as it nears it.
        double near = Math.Clamp(1.0 - (p.Y - RowZoneBottom) / 120.0, 0.0, 1.0);
        double scale = GhostRestScale + (1.0 - GhostRestScale) * near;
        double opacity = drag.Blocked ? BlockedGhostOpacity : GhostOpacity;
        ghost.Pose(p.X - ghost.Width / 2.0, p.Y - WidgetCatalog.WidgetHeight / 2.0 - ShelfCard.RestTop,
            scale, opacity, Reduce);

        var preview = WorkingRow.ToList();
        if (drag.Target is { Direction: 0 } inRow && !drag.Blocked) preview.Insert(inRow.Index, drag.Id);
        SetPreview(preview);
        ShowDropIndicator(zone == DropZone.Above ? zone : null, drag.Blocked);
    }

    /// <summary>Shows the drop hint of the row above or below, red when that row is full. Null hides both.</summary>
    private void ShowDropIndicator(DropZone? zone, bool blocked)
    {
        Brush brush = Resource<Brush>(blocked ? "MutedRedBrush" : "AccentBlueBrush");
        AboveIndicator.Background = brush;
        BelowIndicator.Background = brush;
        AboveIndicator.Visibility = zone == DropZone.Above ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        BelowIndicator.Visibility = zone == DropZone.Below ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    /// <summary>Shows a drag preview order. Only a change re-lays out and resizes the island.</summary>
    private void SetPreview(List<string> preview)
    {
        bool same = _preview is null ? preview.SequenceEqual(WorkingRow) : _preview.SequenceEqual(preview);
        if (same) return;

        _preview = preview;
        Relayout(instant: false);
        LayoutChanged?.Invoke();
    }

    private void FinishDrag()
    {
        Drag drag = _drag!;
        _drag = null;
        _press = null;
        ReleaseCapture(drag.Source);
        EndDragging();
        ShowDropIndicator(null, false);

        if (drag.IsAdd) FinishAdd(drag);
        else FinishReorder(drag);
    }

    private void FinishReorder(Drag drag)
    {
        ShelfCard card = drag.Card;
        bool reduce = Reduce;
        List<string>? preview = _preview;
        _preview = null;
        List<string> origin = WorkingRow;

        if (drag.Zone == DropZone.Tray)
        {
            origin.Remove(drag.Id);
            ExitCard(card);
            PruneRows(origin);
            Relayout(instant: false);
            RebuildTray();
            LayoutChanged?.Invoke();
            return;
        }

        if (drag.Target is { Direction: not 0, Blocked: false } neighbour)
        {
            // Into the row above or below: the card leaves this row and the page follows it.
            card.Mode = CardMode.Normal;
            card.Tune(follow: false);
            SlidePage(neighbour.Direction, () =>
            {
                origin.Remove(drag.Id);
                List<string> target = neighbour.Row ?? CreateRow(neighbour.Direction);
                target.Insert(Math.Min(neighbour.Index, target.Count), drag.Id);
                PruneRows(target);
                Panel.SetZIndex(card.Root, 0);
            });
            return;
        }

        // A drop in this row keeps the preview order; a full neighbour leaves the working row as it was.
        if (preview is not null && drag.Zone == DropZone.Row)
        {
            origin.Clear();
            origin.AddRange(preview);
        }

        // The card springs from wherever it was released into its slot; velocity carries over.
        card.Mode = CardMode.Normal;
        card.Tune(follow: false);
        card.Completion = () => Panel.SetZIndex(card.Root, 0);
        Relayout(instant: reduce);
        if (reduce) RunCompletion(card);
        LayoutChanged?.Invoke();
    }

    private void FinishAdd(Drag drag)
    {
        ShelfCard ghost = drag.Card;
        bool reduce = Reduce;
        _preview = null;
        Landing? landing = drag.Target;

        if (landing is null || landing.Blocked)
        {
            // Released outside the row, or rejected for width: the ghost springs back to its tile and fades.
            ghost.Tune(follow: false);
            ghost.Pose(drag.TileCentre.X - ghost.Width / 2.0,
                drag.TileCentre.Y - WidgetCatalog.WidgetHeight / 2.0 - ShelfCard.RestTop,
                GhostRestScale, 0.0, reduce);
            ghost.Completion = () => RemoveGhost(ghost);
            if (reduce) RunCompletion(ghost);

            Relayout(instant: false);
            LayoutChanged?.Invoke();
            return;
        }

        if (landing.Direction != 0)
        {
            // Into the row above: the page slides to it and the widget takes its place there.
            SlidePage(landing.Direction, () =>
            {
                RemoveGhost(ghost);
                List<string> target = landing.Row ?? CreateRow(landing.Direction);
                target.Insert(Math.Min(landing.Index, target.Count), drag.Id);
                PruneRows(target);
                RebuildTray();
            });
            return;
        }

        int index = Math.Clamp(landing.Index, 0, WorkingRow.Count);
        WorkingRow.Insert(index, drag.Id);

        // The real card starts under the ghost, invisible. The ghost glides into the slot; then the card fades in.
        ShelfCard card = GetCard(ghost.Descriptor);
        card.Mode = CardMode.Normal;
        card.Tune(follow: false);
        card.SetEditing(true);
        if (!IsOnCanvas(card)) WidgetCanvas.Children.Add(card.Root);
        card.Pose(ghost.X.Current, ghost.Y.Current, ghost.Scale.Current, 0.0, instant: true);
        Relayout(instant: false);
        card.Opacity.Snap(0.0);
        card.Apply();

        ghost.Tune(follow: false);
        ghost.Pose(_slotX[drag.Id], 0.0, 1.0, 1.0, reduce);
        ghost.Completion = () =>
        {
            RemoveGhost(ghost);
            card.Opacity.SetTarget(1.0);
            MotionPump.Request(this);
        };
        if (reduce)
        {
            card.Opacity.Snap(1.0);
            card.Apply();
            RunCompletion(ghost);
        }

        RebuildTray();
        LayoutChanged?.Invoke();
    }

    private ShelfCard CreateGhost(WidgetDescriptor descriptor)
    {
        var ghost = new ShelfCard(descriptor, BuildGhostBody(descriptor), removable: false) { Mode = CardMode.Ghost };
        ghost.SetEditing(true);
        return ghost;
    }

    /// <summary>Icon and title of a dragged tile, centred inside the widget-sized placeholder.</summary>
    private static FrameworkElement BuildGhostBody(WidgetDescriptor descriptor)
    {
        var body = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Geometry? icon = WidgetGlyph.Find(descriptor.IconKey);
        if (icon is not null)
        {
            Viewbox glyph = WidgetGlyph.Create(icon, Brushes.White, 40.0);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            body.Children.Add(glyph);
        }
        body.Children.Add(new TextBlock
        {
            Text = descriptor.Title,
            Style = Resource<Style>("TitleTextStyle"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        });
        return body;
    }

    /// <summary>Ends a drag that never reached release (cancel, present/hide, edit session change).</summary>
    private void CancelDrag()
    {
        if (_drag is null && _press is null) return;

        Drag? drag = _drag;
        Press? press = _press;
        _drag = null;
        _press = null;
        _preview = null;
        ShowDropIndicator(null, false);

        if (drag is not null)
        {
            if (drag.IsAdd)
            {
                RemoveGhost(drag.Card);
            }
            else
            {
                drag.Card.Mode = CardMode.Normal;
                drag.Card.Tune(follow: false);
                Panel.SetZIndex(drag.Card.Root, 0);
            }
            ReleaseCapture(drag.Source);
            EndDragging();
        }
        if (press is not null) ReleaseCapture(press.Source);
    }

    private static void ReleaseCapture(FrameworkElement element)
    {
        if (element.IsMouseCaptured) element.ReleaseMouseCapture();
    }

    // ---- Tray buttons ---------------------------------------------------------------------------------------

    private void OnCancelClicked(object sender, MouseButtonEventArgs e)
    {
        CancelDrag();
        _vm?.ExitCustomizeCommand.Execute(null);
        e.Handled = true;
    }

    private void OnCommitClicked(object sender, MouseButtonEventArgs e)
    {
        CancelDrag();
        if (_vm is not null)
        {
            _vm.CommitShelf(_workingRows
                .Select(row => (IReadOnlyList<string>)IslandShapeTable.Resolve(row).Select(d => d.Id).ToArray())
                .ToArray());
        }
        e.Handled = true;
    }

    // ---- Frame pump -----------------------------------------------------------------------------------------

    /// <summary>Steps every spring while something moves; the pump detaches as soon as everything settles.</summary>
    bool IFrameClient.OnFrame(double dt)
    {
        bool busy = false;
        foreach (ShelfCard card in _all)
        {
            card.Step(dt);
            card.Apply();
            busy |= card.Busy;
        }

        // Completions run after stepping: they may add or remove cards.
        List<Action>? ready = null;
        foreach (ShelfCard card in _all)
        {
            if (card.Busy || card.Completion is null) continue;
            (ready ??= new List<Action>()).Add(card.Completion);
            card.Completion = null;
        }
        if (ready is null) return busy;

        foreach (Action action in ready) action();
        return true;   // a completion may have set new targets: run one more frame
    }

    private static T Resource<T>(string key) where T : class =>
        (T)System.Windows.Application.Current.FindResource(key);

    // ---- Gesture state --------------------------------------------------------------------------------------

    /// <summary>Where a dragged widget would land: a row (index in it), or the row above or below (Direction -1 or 1; Row null = a new row).</summary>
    private sealed record Landing(List<string>? Row, int Index, int Direction, bool Blocked);

    /// <summary>Which part of the shelf a dragged pointer is over.</summary>
    private enum DropZone
    {
        Row,
        Above,
        Below,
        Tray,
    }

    /// <summary>A mouse press that may turn into a drag (card or tile) or a click (tile).</summary>
    private sealed class Press
    {
        public Press(FrameworkElement source, Point start, ShelfCard? card = null,
            WidgetDescriptor? tile = null, bool addable = false)
        {
            Source = source;
            Start = start;
            Card = card;
            TileDescriptor = tile;
            Addable = addable;
        }

        public FrameworkElement Source { get; }
        public Point Start { get; }
        public ShelfCard? Card { get; }
        public WidgetDescriptor? TileDescriptor { get; }
        public bool Addable { get; }
    }

    /// <summary>A drag in progress: a card reordered, moved to another row or removed, or a widget added from the tray.</summary>
    private sealed class Drag
    {
        public Drag(bool isAdd, string id, ShelfCard card, FrameworkElement source)
        {
            IsAdd = isAdd;
            Id = id;
            Card = card;
            Source = source;
        }

        public bool IsAdd { get; }
        public string Id { get; }
        /// <summary>The dragged card (reorder) or the ghost placeholder (add).</summary>
        public ShelfCard Card { get; }
        public FrameworkElement Source { get; }

        /// <summary>Reorder: pointer offset from the card's left edge at press.</summary>
        public double GrabX { get; init; }
        /// <summary>Reorder: pointer Y at press.</summary>
        public double PressY { get; init; }
        /// <summary>The zone the pointer is in now.</summary>
        public DropZone Zone { get; set; } = DropZone.Row;
        /// <summary>Where the widget lands if released now; null when released nowhere.</summary>
        public Landing? Target { get; set; }
        /// <summary>The landing row is full (the widget would not fit).</summary>
        public bool Blocked { get; set; }
        /// <summary>Add: centre of the tile the ghost came from (where a rejected ghost returns to).</summary>
        public Point TileCentre { get; set; }
    }
}
