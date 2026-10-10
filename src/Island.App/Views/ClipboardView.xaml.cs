using System.Collections.ObjectModel;
using System.Windows;
using Island.App.ViewModels;
using Island.App.Widgets;
using Island.Core.Clipboard;
using UserControl = System.Windows.Controls.UserControl;

namespace Island.App.Views;

/// <summary>
/// Clipboard history panel (designed for 760 x 232). Reads <see cref="ClipboardHistory"/> through the shelf context.
/// Changes arrive on arbitrary threads; they are marshalled to the UI and coalesced over 50 ms. Entries are keyed by item
/// id, so unchanged items keep their card and only new ones are created or moved.
/// </summary>
public partial class ClipboardView : UserControl
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(50);

    private readonly ObservableCollection<ClipboardEntry> _entries = new();
    private readonly Dictionary<Guid, ClipboardEntry> _byId = new();
    private readonly UiSignal _signal;
    private ShelfContext? _shelf;
    private bool _subscribed;

    public ClipboardView()
    {
        InitializeComponent();
        Cards.ItemsSource = _entries;

        _signal = new UiSignal(Dispatcher, Refresh, CoalesceWindow);

        MoreButton.Click += () => ClearMenu.Visibility = ClearMenu.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        ClearButton.Click += () =>
        {
            ClearMenu.Visibility = Visibility.Collapsed;
            _shelf?.Clipboard.Clear();
        };

        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();

        // Scrolling: the wheel scrolls horizontally, and a press-and-drag on the strip scrolls it too.
        Scroller.PreviewMouseWheel += (_, e) =>
        {
            // Scrolling up with the strip already at its start is left unhandled: the island window uses it to go back to the shelf.
            if (e.Delta > 0 && Scroller.HorizontalOffset <= 0.5) return;
            ScrollBy(-e.Delta * 0.6);
            e.Handled = true;
        };
        Scroller.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pressing = true;
            _dragging = false;
            _suppressClick = false;
            _dragOrigin = e.GetPosition(Scroller);
            _dragStartOffset = Scroller.HorizontalOffset;
        };
        Scroller.PreviewMouseLeftButtonUp += (_, _) => _pressing = false;
        Scroller.MouseMove += (_, e) =>
        {
            if (!_pressing || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;

            double dx = e.GetPosition(Scroller).X - _dragOrigin.X;
            if (!_dragging && Math.Abs(dx) < DragThreshold) return;

            _dragging = true;
            // A drag ends with the release over a card; that release must not copy the card.
            _suppressClick = true;
            ScrollTo(_dragStartOffset - dx);
        };
    }

    private const double DragThreshold = 6.0;

    private bool _pressing;
    private bool _dragging;
    private bool _suppressClick;
    private System.Windows.Point _dragOrigin;
    private double _dragStartOffset;

    private void ScrollBy(double delta) => ScrollTo(Scroller.HorizontalOffset + delta);

    private void ScrollTo(double offset) =>
        Scroller.ScrollToHorizontalOffset(Math.Clamp(offset, 0.0, Scroller.ScrollableWidth));

    /// <summary>Connects the panel to the shelf (history and clipboard service).</summary>
    public void Attach(IslandViewModel vm)
    {
        _shelf = vm.Shelf;
        Subscribe();
    }

    private void Subscribe()
    {
        if (_shelf is null || _subscribed) return;

        _shelf.Clipboard.Changed += OnHistoryChanged;
        _subscribed = true;
        Refresh();
    }

    private void Unsubscribe()
    {
        if (_shelf is null || !_subscribed) return;

        _shelf.Clipboard.Changed -= OnHistoryChanged;
        _subscribed = false;
    }

    /// <summary>Arbitrary thread.</summary>
    private void OnHistoryChanged() => _signal.Signal();

    private void Refresh()
    {
        ShelfContext? shelf = _shelf;
        if (shelf is null) return;

        IReadOnlyList<ClipboardItem> items = shelf.Clipboard.Items;

        var target = new List<ClipboardEntry>(items.Count);
        var keep = new HashSet<Guid>();
        for (int i = 0; i < items.Count; i++)
        {
            ClipboardItem item = items[i];
            if (!_byId.TryGetValue(item.Id, out ClipboardEntry? entry))
            {
                entry = CreateEntry(item, shelf);
                _byId[item.Id] = entry;
            }
            entry.IsLatest = i == 0;
            target.Add(entry);
            keep.Add(item.Id);
        }

        foreach (Guid stale in _byId.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            _byId.Remove(stale);
        }

        // Remove entries that are gone, then move or insert so the collection matches the target order.
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(_entries[i].Id)) _entries.RemoveAt(i);
        }

        for (int i = 0; i < target.Count; i++)
        {
            ClipboardEntry wanted = target[i];
            if (i < _entries.Count && ReferenceEquals(_entries[i], wanted)) continue;

            int current = _entries.IndexOf(wanted);
            if (current >= 0) _entries.Move(current, i);
            else _entries.Insert(i, wanted);
        }

        EmptyText.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private ClipboardEntry CreateEntry(ClipboardItem item, ShelfContext shelf)
    {
        ClipboardKind kind = item.Kind;
        string? text = item.Text;
        byte[]? bytes = item.ImageBytes;
        Guid id = item.Id;

        Action copy = () =>
        {
            if (_suppressClick) return;

            if (kind == ClipboardKind.Image)
            {
                if (bytes is not null) shelf.ClipboardService.SetImage(ClipboardImageCodec.ToBmp(bytes));
            }
            else
            {
                shelf.ClipboardService.SetText(text ?? string.Empty);
            }
        };

        Action delete = () => shelf.Clipboard.Remove(id);
        return new ClipboardEntry(item, copy, delete);
    }
}
