namespace Island.Core.Shelf;

/// <summary>
/// In-memory list of file paths dropped on the shelf's file tray. Max 24 entries, no duplicates (compared
/// case-insensitively, as Windows paths are), newest first. Thread-safe.
/// </summary>
public sealed class FileTray
{
    public const int MaxItems = 24;

    // Guards _paths. Events are raised after the lock is released.
    private readonly object _gate = new();
    private readonly List<string> _paths = new();

    /// <summary>Immutable snapshot, newest first.</summary>
    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (_gate) return _paths.ToArray();
        }
    }

    /// <summary>Raised after Add/Remove/Clear when the list changed. Arbitrary thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// Adds the paths on top, keeping their order (the first dropped path ends up first). A path already in the tray
    /// is moved to the top rather than duplicated. Null or blank paths are ignored; the oldest entries are dropped beyond 24.
    /// </summary>
    public void Add(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var batch = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (batch.Any(existing => PathEquals(existing, path))) continue;
            batch.Add(path);
        }

        if (batch.Count == 0) return;

        lock (_gate)
        {
            _paths.RemoveAll(existing => batch.Any(added => PathEquals(existing, added)));
            _paths.InsertRange(0, batch);
            if (_paths.Count > MaxItems) _paths.RemoveRange(MaxItems, _paths.Count - MaxItems);
        }

        Changed?.Invoke();
    }

    public void Remove(string path)
    {
        if (path is null) return;

        bool removed;
        lock (_gate) removed = _paths.RemoveAll(existing => PathEquals(existing, path)) > 0;

        if (removed) Changed?.Invoke();
    }

    public void Clear()
    {
        bool hadItems;
        lock (_gate)
        {
            hadItems = _paths.Count > 0;
            _paths.Clear();
        }

        if (hadItems) Changed?.Invoke();
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
