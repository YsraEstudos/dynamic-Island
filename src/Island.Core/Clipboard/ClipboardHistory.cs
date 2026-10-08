using System.Text.RegularExpressions;
using Island.Core.Configuration;

namespace Island.Core.Clipboard;

/// <summary>In-memory only (never persisted, for privacy). Newest first. Thread-safe.</summary>
public sealed class ClipboardHistory
{
    private static readonly Regex LinkPattern = new(@"^https?://\S+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ColorPattern = new(@"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.CultureInvariant);

    private readonly Func<IslandSettings> _settings;
    // Guards _items. Events are raised after the lock is released.
    private readonly object _gate = new();
    private readonly List<ClipboardItem> _items = new();

    public ClipboardHistory(Func<IslandSettings> settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>Immutable snapshot, newest first.</summary>
    public IReadOnlyList<ClipboardItem> Items
    {
        get
        {
            lock (_gate) return _items.ToArray();
        }
    }

    /// <summary>Raised after Add/Remove/Clear when the list changed. Arbitrary thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// Adds at the top. An identical entry already present (same Kind and Text, or same Image bytes) is removed
    /// and the new one is inserted on top. Trims the oldest entries to settings.ClipboardMaxItems (at least 1).
    /// Items without content (null or whitespace text for non-image kinds, empty image bytes) are ignored.
    /// </summary>
    public void Add(ClipboardItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!HasContent(item)) return;

        var max = Math.Max(1, _settings().ClipboardMaxItems);
        lock (_gate)
        {
            _items.RemoveAll(existing => IsSameContent(existing, item));
            _items.Insert(0, item);
            if (_items.Count > max) _items.RemoveRange(max, _items.Count - max);
        }

        Changed?.Invoke();
    }

    public void Remove(Guid id)
    {
        bool removed;
        lock (_gate) removed = _items.RemoveAll(item => item.Id == id) > 0;

        if (removed) Changed?.Invoke();
    }

    public void Clear()
    {
        bool hadItems;
        lock (_gate)
        {
            hadItems = _items.Count > 0;
            _items.Clear();
        }

        if (hadItems) Changed?.Invoke();
    }

    /// <summary>http(s) URL => Link; #RGB/#RRGGBB/#RRGGBBAA => Color; else Text. Surrounding whitespace is ignored.</summary>
    public static ClipboardKind Classify(string text)
    {
        if (text is null) return ClipboardKind.Text;

        var trimmed = text.Trim();
        if (LinkPattern.IsMatch(trimmed)) return ClipboardKind.Link;
        if (ColorPattern.IsMatch(trimmed)) return ClipboardKind.Color;
        return ClipboardKind.Text;
    }

    private static bool HasContent(ClipboardItem item) => item.Kind == ClipboardKind.Image
        ? item.ImageBytes is { Length: > 0 }
        : !string.IsNullOrWhiteSpace(item.Text);

    private static bool IsSameContent(ClipboardItem a, ClipboardItem b)
    {
        if (a.Kind != b.Kind) return false;
        return a.Kind == ClipboardKind.Image
            ? a.ImageBytes is not null && b.ImageBytes is not null && a.ImageBytes.AsSpan().SequenceEqual(b.ImageBytes)
            : string.Equals(a.Text, b.Text, StringComparison.Ordinal);
    }
}
