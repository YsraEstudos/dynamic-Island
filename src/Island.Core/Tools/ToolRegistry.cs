namespace Island.Core.Tools;

/// <summary>
/// Holds the long-lived model behind each small tool widget (calculator, counter, timers, ...), keyed by its type.
/// A widget asks for its model with <see cref="GetOrCreate{T}"/>, so the state survives the widget being rebuilt
/// when the shelf is reopened or re-laid out (a running timer must not reset). Disposable models are disposed with the registry.
/// </summary>
public sealed class ToolRegistry : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, object> _items = new();
    private bool _disposed;

    /// <summary>Returns the model of type <typeparamref name="T"/>, creating it with <paramref name="factory"/> on first use.</summary>
    public T GetOrCreate<T>(Func<T> factory) where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_items.TryGetValue(typeof(T), out object? existing)) return (T)existing;

            T created = factory();
            _items[typeof(T)] = created;
            return created;
        }
    }

    public void Dispose()
    {
        List<IDisposable> disposables;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            disposables = _items.Values.OfType<IDisposable>().ToList();
            _items.Clear();
        }

        foreach (IDisposable item in disposables) item.Dispose();
    }
}
