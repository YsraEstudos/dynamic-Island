namespace Island.Core.Performance;

/// <summary>Fixed-capacity buffer that overwrites the oldest item. Not thread-safe: callers lock.</summary>
public sealed class RingBuffer<T>
{
    private readonly T[] _items;
    private int _next;
    private int _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count => _count;

    public void Add(T item)
    {
        _items[_next] = item;
        _next = (_next + 1) % _items.Length;
        if (_count < _items.Length) _count++;
    }

    /// <summary>The stored items, oldest first.</summary>
    public T[] ToArray()
    {
        var result = new T[_count];
        int start = _count < _items.Length ? 0 : _next;
        for (int i = 0; i < _count; i++) result[i] = _items[(start + i) % _items.Length];
        return result;
    }
}
