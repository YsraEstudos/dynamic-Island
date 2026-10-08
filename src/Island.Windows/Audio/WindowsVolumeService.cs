using Island.Core.Abstractions;
using Island.Core.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Island.Windows.Audio;

/// <summary>
/// Master volume of the default render endpoint, fully callback-driven: WASAPI volume notifications
/// update the value, and IMMNotificationClient re-attaches when the default device changes.
/// </summary>
public sealed class WindowsVolumeService : IVolumeService, IMMNotificationClient
{
    // Multimedia is the role the system volume flyout controls.
    private const Role VolumeRole = Role.Multimedia;

    private readonly ILogger<WindowsVolumeService>? _logger;

    // Serializes device lifecycle work. COM callbacks never wait on it: they queue work to the thread pool,
    // so unregistering a callback cannot deadlock against an in-flight notification.
    private readonly object _deviceGate = new();

    // Guards _current. Never held across COM calls or event invocation.
    private readonly object _stateGate = new();

    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private AudioEndpointVolume? _endpoint;
    private AudioEndpointVolumeNotificationDelegate? _volumeHandler;   // subscribed to _endpoint; removed on detach
    private volatile string? _deviceId;
    private int _generation;        // bumped on every attach/detach; stale notifications are dropped
    private int _reattachPending;   // coalesces bursts of default-device notifications
    private bool _initialized;
    private bool _disposed;
    private VolumeInfo _current = new(0, false);

    public WindowsVolumeService(ILogger<WindowsVolumeService>? logger = null)
    {
        _logger = logger;
    }

    public event EventHandler<VolumeInfo>? VolumeChanged;

    public VolumeInfo Current
    {
        get { lock (_stateGate) return _current; }
    }

    /// <summary>Attaches to the default render endpoint and registers for device notifications. Idempotent.</summary>
    public void Initialize()
    {
        lock (_deviceGate)
        {
            if (_initialized || _disposed) return;
            _initialized = true;
            try
            {
                _enumerator = new MMDeviceEnumerator();
                _enumerator.RegisterEndpointNotificationCallback(this);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Audio device enumerator unavailable; volume control is disabled.");
            }
            AttachDefaultEndpointLocked();
        }
    }

    public void SetLevel(int level0to100)
    {
        int level = Math.Clamp(level0to100, 0, 100);
        lock (_deviceGate)
        {
            try
            {
                if (_endpoint is { } endpoint)
                    endpoint.MasterVolumeLevelScalar = level / 100f;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to set master volume.");
            }
        }
    }

    public void SetMuted(bool muted)
    {
        lock (_deviceGate)
        {
            try
            {
                if (_endpoint is { } endpoint)
                    endpoint.Mute = muted;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to set mute state.");
            }
        }
    }

    /// <summary>Must be called with <see cref="_deviceGate"/> held.</summary>
    private void AttachDefaultEndpointLocked()
    {
        DetachEndpointLocked();
        int gen = Interlocked.Increment(ref _generation);

        MMDevice? device = null;
        AudioEndpointVolume? endpoint = null;
        AudioEndpointVolumeNotificationDelegate? handler = null;
        try
        {
            if (_enumerator is null) throw new InvalidOperationException("No device enumerator.");

            device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, VolumeRole);
            endpoint = device.AudioEndpointVolume;
            handler = data => OnVolumeNotification(gen, data);
            endpoint.OnVolumeNotification += handler;

            var info = ToVolumeInfo(endpoint.MasterVolumeLevelScalar, endpoint.Mute);
            _device = device;
            _endpoint = endpoint;
            _volumeHandler = handler;
            _deviceId = device.ID;
            // Ownership moved to the fields; the catch block must not dispose them.
            device = null;
            endpoint = null;

            Publish(gen, info, force: true);
        }
        catch (Exception ex)
        {
            // Typically no render endpoint (E_NOTFOUND) or the default device is disabled.
            _logger?.LogInformation(ex, "No usable default render endpoint; volume reads as 0.");
            if (endpoint is not null && handler is not null) endpoint.OnVolumeNotification -= handler;
            SafeDispose(endpoint);
            SafeDispose(device);
            Publish(gen, new VolumeInfo(0, false), force: true);
        }
    }

    /// <summary>Must be called with <see cref="_deviceGate"/> held.</summary>
    private void DetachEndpointLocked()
    {
        // Invalidates any notification still in flight for the old device.
        Interlocked.Increment(ref _generation);
        if (_endpoint is not null && _volumeHandler is not null)
            _endpoint.OnVolumeNotification -= _volumeHandler;
        _volumeHandler = null;
        SafeDispose(_endpoint);
        SafeDispose(_device);
        _endpoint = null;
        _device = null;
        _deviceId = null;
    }

    private void OnVolumeNotification(int gen, AudioVolumeNotificationData data)
    {
        try
        {
            Publish(gen, ToVolumeInfo(data.MasterVolume, data.Muted), force: false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Volume notification handling failed.");
        }
    }

    /// <summary>
    /// Updates <see cref="Current"/> and raises <see cref="VolumeChanged"/> when the value differs,
    /// or always when <paramref name="force"/> is set. Stale generations are dropped.
    /// </summary>
    private void Publish(int gen, VolumeInfo info, bool force)
    {
        lock (_stateGate)
        {
            if (gen != Volatile.Read(ref _generation)) return;
            if (!force && info == _current) return;
            _current = info;
        }
        RaiseVolumeChanged(info);
    }

    private void RaiseVolumeChanged(VolumeInfo info)
    {
        try
        {
            VolumeChanged?.Invoke(this, info);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "VolumeChanged handler threw.");
        }
    }

    private static VolumeInfo ToVolumeInfo(float scalar, bool muted) => new(ToLevel(scalar), muted);

    private static int ToLevel(float scalar)
    {
        if (!float.IsFinite(scalar)) return 0;
        return (int)Math.Clamp(Math.Round(scalar * 100d, MidpointRounding.AwayFromZero), 0d, 100d);
    }

    private static void SafeDispose(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception)
        {
            // Disposal of a device that was unplugged mid-flight can throw; nothing to recover.
        }
    }

    /// <summary>
    /// Runs the re-attach off the COM notification thread. Bursts coalesce into one pending item, and each
    /// run reads the current default device, so the last run always reflects the final default.
    /// </summary>
    private void ScheduleReattach()
    {
        if (Interlocked.Exchange(ref _reattachPending, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(_ => ReattachFromWorker());
    }

    private void ReattachFromWorker()
    {
        try
        {
            lock (_deviceGate)
            {
                Volatile.Write(ref _reattachPending, 0);
                if (!_initialized || _disposed) return;
                AttachDefaultEndpointLocked();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Re-attaching to the default audio device failed.");
        }
    }

    public void Dispose()
    {
        lock (_deviceGate)
        {
            if (_disposed) return;
            _disposed = true;

            if (_enumerator is not null)
            {
                try
                {
                    _enumerator.UnregisterEndpointNotificationCallback(this);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Unregistering audio notifications failed.");
                }
            }

            DetachEndpointLocked();
            SafeDispose(_enumerator);
            _enumerator = null;
        }
    }

    // ---- IMMNotificationClient (invoked on arbitrary COM threads; must not throw) ----

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == VolumeRole) ScheduleReattach();
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => OnDeviceTopologyChanged(deviceId);

    public void OnDeviceRemoved(string deviceId) => OnDeviceTopologyChanged(deviceId);

    public void OnDeviceAdded(string pwstrDeviceId) => ScheduleReattach();

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Property changes (names, icons, formats) do not affect volume.
    }

    private void OnDeviceTopologyChanged(string deviceId)
    {
        if (deviceId == _deviceId) ScheduleReattach();
    }
}
