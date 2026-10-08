using Island.Core.Abstractions;
using Island.Core.Models;
using Microsoft.Extensions.Logging;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace Island.Windows.Devices;

/// <summary>Publishes real USB and Bluetooth presence transitions without treating pairing as a connection.</summary>
public sealed class WindowsDeviceNoticeSource : ISystemNoticeSource
{
    private const string UsbInterfaceGuid = "{A5DCBF10-6530-11D2-901F-00C04FB951ED}";
    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string ContainerProperty = "System.Devices.ContainerId";
    private const string AepContainerProperty = "System.Devices.Aep.ContainerId";
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";
    private const string InstanceProperty = "System.Devices.DeviceInstanceId";
    private const string DisplayNameProperty = "System.ItemNameDisplay";

    private readonly object _gate = new();
    private readonly DevicePresenceTracker _tracker = new();
    private readonly ILogger? _logger;
    private readonly List<WatcherState> _watchers = new();
    private readonly Queue<Notice> _pendingNotices = new();
    private bool _started;
    private bool _disposed;
    private bool _drainingNotices;
    private readonly Dictionary<DevicePresenceKind, int> _baselineRemaining = new();

    public WindowsDeviceNoticeSource(ILogger<WindowsDeviceNoticeSource>? logger = null) => _logger = logger;

    public event EventHandler<Notice>? NoticeRaised;

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
            _baselineRemaining[DevicePresenceKind.Usb] = 1;
            _baselineRemaining[DevicePresenceKind.Bluetooth] = 2;
        }

        StartWatcher(DevicePresenceKind.Usb, CreateUsbWatcher);
        StartWatcher(DevicePresenceKind.Bluetooth, CreateBluetoothWatcher);
        StartWatcher(DevicePresenceKind.Bluetooth, CreateBluetoothLeWatcher);
    }

    private void StartWatcher(DevicePresenceKind kind, Func<DeviceWatcher> factory)
    {
        DeviceWatcher watcher;
        try { watcher = factory(); }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not create {Kind} device watcher.", kind);
            CompleteBaseline(kind);
            return;
        }

        var state = new WatcherState(this, kind, watcher);
        watcher.Added += state.OnAdded;
        watcher.Updated += state.OnUpdated;
        watcher.Removed += state.OnRemoved;
        watcher.EnumerationCompleted += state.OnEnumerationCompleted;
        watcher.Stopped += state.OnStopped;

        Exception? startError = null;
        bool skipped;
        lock (_gate)
        {
            skipped = _disposed;
            if (!skipped)
            {
                _watchers.Add(state);
                try { watcher.Start(); }
                catch (Exception ex) { startError = ex; }
            }
        }

        if (skipped)
        {
            Detach(state);
            try { watcher.Stop(); } catch { }
            return;
        }

        if (startError is not null)
        {
            _logger?.LogWarning(startError, "Could not start {Kind} device watcher.", kind);
            CompleteBaseline(state);
            Detach(state);
        }
    }

    private DeviceWatcher CreateUsbWatcher() => DeviceInformation.CreateWatcher(
        $"System.Devices.InterfaceClassGuid:=\"{UsbInterfaceGuid}\"",
        new[] { ContainerProperty, AepContainerProperty, DisplayNameProperty, InstanceProperty },
        DeviceInformationKind.DeviceInterface);

    private static DeviceWatcher CreateBluetoothWatcher() => DeviceInformation.CreateWatcher(
        global::Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelector(),
        RequestedBluetoothProperties,
        DeviceInformationKind.AssociationEndpoint);

    private static DeviceWatcher CreateBluetoothLeWatcher() => DeviceInformation.CreateWatcher(
        global::Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelector(),
        RequestedBluetoothProperties,
        DeviceInformationKind.AssociationEndpoint);

    private static readonly string[] RequestedBluetoothProperties =
    {
        ConnectedProperty, ContainerProperty, AepContainerProperty, AddressProperty, DisplayNameProperty,
    };

    private void OnAdded(WatcherState state, DeviceInformation info)
    {
        if (state.Kind == DevicePresenceKind.Usb)
        {
            Apply(state, EndpointKey(state.Kind, info.Id), GroupId(state.Kind, info), connected: true, Name(info));
            return;
        }

        Apply(state, EndpointKey(state.Kind, info.Id), GroupId(state.Kind, info), IsConnected(info), Name(info));
    }

    private void OnUpdated(WatcherState state, DeviceInformationUpdate update)
    {
        string endpointId = EndpointKey(state.Kind, update.Id);
        try
        {
            // Update payloads are partial. The tracker retains the old name, while the watcher identity supplies
            // the stable endpoint key. DeviceInformation.CreateFromIdAsync is deliberately avoided here: callbacks
            // must stay non-blocking and the requested properties are present in normal watcher updates.
            lock (_gate)
            {
                if (_disposed) return;
                var previous = _tracker.Current(endpointId);
                string groupId = ValidGroup(StringProperty(update, ContainerProperty))
                    ?? ValidGroup(StringProperty(update, AepContainerProperty))
                    ?? ValidGroup(StringProperty(update, AddressProperty))
                    ?? previous?.GroupId
                    ?? endpointId;
                string? name = StringProperty(update, DisplayNameProperty);
                bool connected = state.Kind == DevicePresenceKind.Usb
                    || (update.Properties.ContainsKey(ConnectedProperty)
                        ? BoolProperty(update, ConnectedProperty)
                        : previous?.Connected == true);
                QueueTransitionsLocked(_tracker.AddOrUpdate(state.Kind, endpointId, groupId, connected, name));
            }
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "Ignoring malformed device update {Id}.", endpointId); }
    }

    private void OnRemoved(WatcherState state, DeviceInformationUpdate update) => Remove(state, EndpointKey(state.Kind, update.Id));

    private void Apply(WatcherState state, string endpointId, string groupId, bool connected, string? name)
    {
        IReadOnlyList<DevicePresenceTransition> transitions;
        lock (_gate)
        {
            if (_disposed) return;
            transitions = _tracker.AddOrUpdate(state.Kind, endpointId, groupId, connected, name);
            QueueTransitionsLocked(transitions);
        }
    }

    private void Remove(WatcherState state, string endpointId)
    {
        IReadOnlyList<DevicePresenceTransition> transitions;
        lock (_gate)
        {
            if (_disposed) return;
            transitions = _tracker.Remove(endpointId);
            QueueTransitionsLocked(transitions);
        }
    }

    private void QueueTransitionsLocked(IReadOnlyList<DevicePresenceTransition> transitions)
    {
        bool startDrain = false;
        if (_disposed) return;
        foreach (DevicePresenceTransition transition in transitions)
        {
            bool usb = transition.Kind == DevicePresenceKind.Usb;
            string family = usb ? "USB" : "Bluetooth";
            string title = transition.Connected
                ? $"{family} conectado"
                : usb ? "USB removido" : "Bluetooth desconectado";
            string glyph = usb
                ? transition.Connected ? "usb-on" : "usb-off"
                : transition.Connected ? "bluetooth-on" : "bluetooth-off";
            string subtitle = string.Equals(transition.Name, transition.GroupId, StringComparison.OrdinalIgnoreCase)
                ? $"Dispositivo {family}"
                : transition.Name;
            _pendingNotices.Enqueue(new Notice(title, subtitle, glyph));
        }
        if (_pendingNotices.Count != 0 && !_drainingNotices)
        {
            _drainingNotices = true;
            startDrain = true;
        }
        if (startDrain) _ = Task.Run(DrainNotices);
    }

    private void DrainNotices()
    {
        while (true)
        {
            Notice notice;
            lock (_gate)
            {
                if (_disposed)
                {
                    _pendingNotices.Clear();
                    _drainingNotices = false;
                    return;
                }
                if (_pendingNotices.Count == 0)
                {
                    _drainingNotices = false;
                    return;
                }
                notice = _pendingNotices.Dequeue();
            }
            try { NoticeRaised?.Invoke(this, notice); }
            catch (Exception ex) { _logger?.LogWarning(ex, "System device notice subscriber failed."); }
        }
    }

    private void OnEnumerationCompleted(WatcherState state) => CompleteBaseline(state);

    private void OnStopped(WatcherState state)
    {
        if (!state.EnumerationCompleted) CompleteBaseline(state);
    }

    private void CompleteBaseline(WatcherState state)
    {
        lock (_gate)
        {
            if (state.EnumerationCompleted) return;
            state.EnumerationCompleted = true;
            if (!_baselineRemaining.TryGetValue(state.Kind, out int remaining)) return;
            remaining--;
            _baselineRemaining[state.Kind] = remaining;
            if (remaining == 0) _tracker.CompleteBaseline(state.Kind);
        }
    }

    private void CompleteBaseline(DevicePresenceKind kind)
    {
        lock (_gate)
        {
            if (!_baselineRemaining.TryGetValue(kind, out int remaining)) return;
            remaining--;
            _baselineRemaining[kind] = remaining;
            if (remaining == 0) _tracker.CompleteBaseline(kind);
        }
    }

    public void Dispose()
    {
        WatcherState[] states;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pendingNotices.Clear();
            states = _watchers.ToArray();
            _watchers.Clear();
        }

        foreach (WatcherState state in states)
        {
            Detach(state);
            try { state.Watcher.Stop(); }
            catch (Exception ex) { _logger?.LogDebug(ex, "Stopping device watcher failed."); }
        }
    }

    private static string GroupId(DevicePresenceKind kind, DeviceInformation info) =>
        ValidGroup(StringProperty(info, ContainerProperty))
        ?? ValidGroup(StringProperty(info, AepContainerProperty))
        ?? ValidGroup(kind == DevicePresenceKind.Bluetooth ? StringProperty(info, AddressProperty) : null)
        ?? ValidGroup(StringProperty(info, InstanceProperty))
        ?? info.Id;

    private static string EndpointKey(DevicePresenceKind kind, string id) => $"{kind}:{id}";

    private static string? ValidGroup(string? value) =>
        string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out Guid guid) && guid == Guid.Empty
            ? null
            : value;

    private static string? Name(DeviceInformation info) =>
        string.IsNullOrWhiteSpace(info.Name) ? StringProperty(info, DisplayNameProperty) : info.Name;

    private static bool IsConnected(DeviceInformation info) => BoolProperty(info, ConnectedProperty);

    private static string? StringProperty(DeviceInformation info, string key) =>
        info.Properties.TryGetValue(key, out object? value) ? value?.ToString() : null;

    private static bool BoolProperty(DeviceInformation info, string key) =>
        info.Properties.TryGetValue(key, out object? value) && value is bool connected && connected;

    private static string? StringProperty(DeviceInformationUpdate info, string key) =>
        info.Properties.TryGetValue(key, out object? value) ? value?.ToString() : null;

    private static bool BoolProperty(DeviceInformationUpdate info, string key) =>
        info.Properties.TryGetValue(key, out object? value) && value is bool connected && connected;

    private static void Detach(WatcherState state)
    {
        state.Watcher.Added -= state.OnAdded;
        state.Watcher.Updated -= state.OnUpdated;
        state.Watcher.Removed -= state.OnRemoved;
        state.Watcher.EnumerationCompleted -= state.OnEnumerationCompleted;
        state.Watcher.Stopped -= state.OnStopped;
    }

    private sealed class WatcherState
    {
        public WatcherState(WindowsDeviceNoticeSource owner, DevicePresenceKind kind, DeviceWatcher watcher)
        {
            Owner = owner;
            Kind = kind;
            Watcher = watcher;
            OnAdded = (_, info) => owner.OnAdded(this, info);
            OnUpdated = (_, update) => owner.OnUpdated(this, update);
            OnRemoved = (_, update) => owner.OnRemoved(this, update);
            OnEnumerationCompleted = (_, _) => owner.OnEnumerationCompleted(this);
            OnStopped = (_, _) => owner.OnStopped(this);
        }

        public DevicePresenceKind Kind { get; }
        public DeviceWatcher Watcher { get; }
        public bool EnumerationCompleted { get; set; }
        public TypedEventHandler<DeviceWatcher, DeviceInformation> OnAdded { get; }
        public TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> OnUpdated { get; }
        public TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> OnRemoved { get; }
        public TypedEventHandler<DeviceWatcher, object> OnEnumerationCompleted { get; }
        public TypedEventHandler<DeviceWatcher, object> OnStopped { get; }
        private WindowsDeviceNoticeSource Owner { get; }
    }
}
