namespace Island.Windows.Devices;

internal enum DevicePresenceKind
{
    Usb,
    Bluetooth,
}

internal readonly record struct DevicePresenceTransition(
    DevicePresenceKind Kind,
    string GroupId,
    bool Connected,
    string Name);

internal sealed class DevicePresenceTracker
{
    private sealed record Endpoint(
        DevicePresenceKind Kind,
        string GroupId,
        bool Connected,
        string? Name);

    private readonly Dictionary<string, Endpoint> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<DevicePresenceKind> _completedKinds = new();

    public IReadOnlyList<DevicePresenceTransition> AddOrUpdate(
        DevicePresenceKind kind,
        string endpointId,
        string groupId,
        bool connected,
        string? name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        _endpoints.TryGetValue(endpointId, out Endpoint? old);
        bool sameGroup = old is not null
            && old.Kind == kind
            && string.Equals(old.GroupId, groupId, StringComparison.OrdinalIgnoreCase);
        bool groupEnriched = old is not null && old.Kind == kind && !sameGroup;
        bool before = sameGroup && old!.Connected || HasConnectedEndpoint(kind, groupId);

        string? retainedName = string.IsNullOrWhiteSpace(name) ? old?.Name : name.Trim();
        var updated = new Endpoint(kind, groupId, connected, retainedName);
        _endpoints.Remove(endpointId);
        _endpoints[endpointId] = updated;

        // A watcher can first report an endpoint ID and later enrich it with ContainerId. Re-key silently;
        // otherwise the same already-connected device would appear as a fresh connection.
        if (groupEnriched && old!.Connected == connected)
            return Array.Empty<DevicePresenceTransition>();

        bool after = HasConnectedEndpoint(kind, groupId);
        if (!_completedKinds.Contains(kind) || before == after)
            return Array.Empty<DevicePresenceTransition>();

        return Transition(kind, groupId, after, updated.Name);
    }

    public IReadOnlyList<DevicePresenceTransition> Remove(string endpointId)
    {
        if (!_endpoints.Remove(endpointId, out Endpoint? old) || old is null || !_completedKinds.Contains(old.Kind))
            return Array.Empty<DevicePresenceTransition>();

        if (!old.Connected || HasConnectedEndpoint(old.Kind, old.GroupId))
            return Array.Empty<DevicePresenceTransition>();

        return Transition(old.Kind, old.GroupId, false, NameFor(old.Kind, old.GroupId, old.Name));
    }

    public void CompleteBaseline(DevicePresenceKind kind) => _completedKinds.Add(kind);

    public void CompleteBaseline()
    {
        _completedKinds.Add(DevicePresenceKind.Usb);
        _completedKinds.Add(DevicePresenceKind.Bluetooth);
    }

    public (string GroupId, bool Connected)? Current(string endpointId) =>
        _endpoints.TryGetValue(endpointId, out Endpoint? endpoint)
            ? (endpoint.GroupId, endpoint.Connected)
            : null;

    private IReadOnlyList<DevicePresenceTransition> Transition(
        DevicePresenceKind kind, string groupId, bool connected, string? name)
    {
        return new[] { new DevicePresenceTransition(kind, groupId, connected, NameFor(kind, groupId, name)) };
    }

    private bool HasConnectedEndpoint(DevicePresenceKind kind, string groupId) =>
        _endpoints.Values.Any(endpoint =>
            endpoint.Kind == kind
            &&
            string.Equals(endpoint.GroupId, groupId, StringComparison.OrdinalIgnoreCase)
            && endpoint.Connected);

    private string NameFor(DevicePresenceKind kind, string groupId, string? fallback)
    {
        string? name = _endpoints.Values
            .Where(endpoint => endpoint.Kind == kind
                && string.Equals(endpoint.GroupId, groupId, StringComparison.OrdinalIgnoreCase))
            .Select(endpoint => endpoint.Name)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return name ?? (string.IsNullOrWhiteSpace(fallback) ? groupId : fallback);
    }
}
