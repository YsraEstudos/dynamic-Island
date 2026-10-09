using System.Diagnostics;
using Island.Core.Abstractions;
using Island.Core.Audio;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Island.Windows.Audio;

/// <summary>
/// Per-application audio sessions on the default render device, event-driven: WASAPI session notifications add and remove
/// sessions, and session callbacks report volume, mute, state and disconnects. Default-device changes re-attach through
/// IMMNotificationClient. Every Core Audio object lives on one MTA thread (<see cref="AudioMixerThread"/>); the UI only reads
/// the published snapshot. Peaks are read only while a <see cref="BeginMetering"/> scope is open and some session is audible.
/// </summary>
public sealed class WindowsAudioMixerService : IAudioMixerService, IMMNotificationClient
{
    private readonly ILogger<WindowsAudioMixerService>? _logger;
    private readonly AudioMixerThread _thread;
    private readonly object _gate = new();
    private IReadOnlyList<AppAudioSession> _sessions = Array.Empty<AppAudioSession>(); // guarded by _gate
    private bool _started;     // guarded by _gate
    private bool _disposed;    // guarded by _gate

    // Mixer thread only.
    private readonly Dictionary<string, MixerSessionHandle> _handles = new(StringComparer.Ordinal);
    private readonly Stopwatch _peakClock = Stopwatch.StartNew();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private AudioSessionManager? _manager;
    private int _meterRefs;
    private long _lastPeakTicks;

    // Set from any thread to coalesce bursts of work into one queued action.
    private int _publishQueued;
    private int _reconcileQueued;

    public WindowsAudioMixerService(ILogger<WindowsAudioMixerService>? logger = null)
    {
        _logger = logger;
        _thread = new AudioMixerThread(OnMeterTick, logger);
    }

    /// <summary>Raised on the mixer thread. Subscribers marshal to the UI and must not block.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<AppAudioSession> Sessions
    {
        get
        {
            lock (_gate) return _sessions;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }

        _thread.Start();
        _thread.Post(AttachEnumerator);
    }

    public void SetVolume(string sessionId, int level0to100)
    {
        int level = AudioVolumeMath.ClampLevel(level0to100);
        _thread.Post(() =>
        {
            if (_handles.TryGetValue(sessionId, out MixerSessionHandle? handle)) handle.SetVolume(level);
            Publish();
        });
    }

    public void SetMuted(string sessionId, bool muted)
    {
        _thread.Post(() =>
        {
            if (_handles.TryGetValue(sessionId, out MixerSessionHandle? handle)) handle.SetMuted(muted);
            Publish();
        });
    }

    public IDisposable BeginMetering()
    {
        lock (_gate)
        {
            if (_disposed) return new MeterScope(null);
        }

        _thread.Post(() =>
        {
            _meterRefs++;
            Publish();
        });
        return new MeterScope(this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _thread.Post(Shutdown);
        _thread.Stop();
    }

    // ---- Mixer thread ----

    private void AttachEnumerator()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Audio device enumerator unavailable; the app mixer is empty.");
            return;
        }

        AttachDefaultDevice();
    }

    /// <summary>Drops the old device's sessions and listens to the new default endpoint's session manager.</summary>
    private void AttachDefaultDevice()
    {
        ReleaseDevice();
        if (_enumerator is null) return;

        try
        {
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _manager = _device.AudioSessionManager;
            _manager.OnSessionCreated += OnSessionCreated;
        }
        catch (Exception ex)
        {
            // Typically no render endpoint, or the default device is disabled.
            _logger?.LogInformation(ex, "No default render endpoint; the app mixer is empty.");
            ReleaseDevice();
        }

        Reconcile();
    }

    /// <summary>
    /// Brings the handles in line with the session list of the current manager. New sessions get a handle, sessions that
    /// are gone (or disconnected) are disposed. Each session is kept as one handle, so a volume slider keeps its session.
    /// </summary>
    private void Reconcile()
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        bool complete = false;

        if (_manager is not null)
        {
            try
            {
                // NAudio caches the session list; refreshing it is what makes new sessions visible.
                _manager.RefreshSessions();
                SessionCollection? sessions = _manager.Sessions;
                if (sessions is not null)
                {
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        AudioSessionControl? control = sessions[i];
                        if (control is not null) TrackSession(control, present);
                    }
                    complete = true;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Listing audio sessions failed; keeping the current list.");
            }
        }

        // An incomplete listing must not remove sessions that are probably still there.
        if (complete || _manager is null) RemoveMissing(present);
        Publish();
    }

    private void TrackSession(AudioSessionControl control, HashSet<string> present)
    {
        string id;
        int processId;
        bool systemSounds;
        try
        {
            systemSounds = control.IsSystemSoundsSession;
            id = control.GetSessionInstanceIdentifier ?? string.Empty;
            processId = (int)control.GetProcessID;
        }
        catch (Exception)
        {
            control.Dispose();
            return;
        }

        // System sounds are not an app, and an unknown id or process cannot be shown.
        if (systemSounds || processId <= 0 || id.Length == 0)
        {
            control.Dispose();
            return;
        }

        // The same session can be listed twice; keep one handle and drop the extra control object.
        if (!present.Add(id) || _handles.ContainsKey(id))
        {
            control.Dispose();
            return;
        }

        try
        {
            _handles.Add(id, MixerSessionHandle.Create(control, id, processId, OnHandleChanged));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not track an audio session of process {ProcessId}.", processId);
            present.Remove(id);
        }
    }

    private void RemoveMissing(HashSet<string> present)
    {
        List<string> gone = _handles
            .Where(pair => !present.Contains(pair.Key) || pair.Value.IsDisconnected)
            .Select(pair => pair.Key)
            .ToList();

        foreach (string id in gone)
        {
            _handles[id].Dispose();
            _handles.Remove(id);
        }
    }

    private void ReleaseDevice()
    {
        foreach (MixerSessionHandle handle in _handles.Values) handle.Dispose();
        _handles.Clear();

        if (_manager is not null)
        {
            _manager.OnSessionCreated -= OnSessionCreated;
            _manager.Dispose();
            _manager = null;
        }

        _device?.Dispose();
        _device = null;
    }

    /// <summary>Publishes the current state. Runs on the mixer thread, so it may read handles directly.</summary>
    private void Publish()
    {
        UpdateMeteringState();

        IReadOnlyList<AppAudioSession> next = _handles.Values.Select(handle => handle.ToSession()).ToArray();
        bool changed;
        lock (_gate)
        {
            changed = !_sessions.SequenceEqual(next);
            if (changed) _sessions = next;
        }

        if (changed) RaiseChanged();
    }

    /// <summary>Metering runs only while a scope is open and some session is playing or still decaying.</summary>
    private void UpdateMeteringState()
    {
        bool wanted = _meterRefs > 0 && _handles.Values.Any(handle => handle.IsActive || PeakSmoother.IsAudible(handle.Peak));
        if (!wanted)
        {
            foreach (MixerSessionHandle handle in _handles.Values) handle.Peak = 0.0;
        }

        _thread.MeteringEnabled = wanted;
    }

    /// <summary>Runs about every 40 ms while metering is on: reads each playing session's peak and smooths it.</summary>
    private void OnMeterTick()
    {
        long now = _peakClock.ElapsedTicks;
        double seconds = (now - _lastPeakTicks) / (double)Stopwatch.Frequency;
        _lastPeakTicks = now;

        foreach (MixerSessionHandle handle in _handles.Values)
        {
            double raw = handle.IsActive ? handle.ReadPeak() : 0.0;
            handle.Peak = PeakSmoother.Step(handle.Peak, raw, seconds);
        }

        Publish();
    }

    private void Shutdown()
    {
        _thread.MeteringEnabled = false;
        if (_enumerator is not null)
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(this);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Unregistering audio device notifications failed.");
            }
        }

        ReleaseDevice();
        _enumerator?.Dispose();
        _enumerator = null;

        lock (_gate) _sessions = Array.Empty<AppAudioSession>();
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Audio mixer listener threw.");
        }
    }

    // ---- Callbacks from Core Audio threads: only queue work ----

    private void OnSessionCreated(object sender, IAudioSessionControl newSession) => ScheduleReconcile();

    /// <summary>A disconnect changes the session list; anything else only changes values.</summary>
    private void OnHandleChanged(MixerSessionHandle handle)
    {
        if (handle.IsDisconnected) ScheduleReconcile();
        else SchedulePublish();
    }

    private void ScheduleReconcile()
    {
        if (Interlocked.Exchange(ref _reconcileQueued, 1) != 0) return;
        _thread.Post(() =>
        {
            Volatile.Write(ref _reconcileQueued, 0);
            Reconcile();
        });
    }

    private void SchedulePublish()
    {
        if (Interlocked.Exchange(ref _publishQueued, 1) != 0) return;
        _thread.Post(() =>
        {
            Volatile.Write(ref _publishQueued, 0);
            Publish();
        });
    }

    // ---- IMMNotificationClient (arbitrary COM threads; must not throw) ----

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) _thread.Post(AttachDefaultDevice);
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        // A new device does not change the default, so the sessions stay as they are.
    }

    public void OnDeviceRemoved(string deviceId)
    {
        // Removing the default device also raises OnDefaultDeviceChanged, which re-attaches.
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        // Covered by OnDefaultDeviceChanged when it matters for the default device.
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Device names and formats do not affect the session list.
    }

    /// <summary>Metering scope handed to the caller. Disposing it releases the meter reference once.</summary>
    private sealed class MeterScope(WindowsAudioMixerService? owner) : IDisposable
    {
        private WindowsAudioMixerService? _owner = owner;

        public void Dispose()
        {
            WindowsAudioMixerService? owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;

            owner._thread.Post(() =>
            {
                if (owner._meterRefs > 0) owner._meterRefs--;
                owner.Publish();
            });
        }
    }
}
