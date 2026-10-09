using Island.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Island.Windows.Audio;

/// <summary>
/// One audio session of the default device, owned by the mixer thread. Core Audio calls its event methods on its own
/// threads, so they only record state under a lock and report a change; they never call into COM. COM calls
/// (volume, mute, meter, dispose) happen on the mixer thread only.
/// </summary>
internal sealed class MixerSessionHandle : IAudioSessionEventsHandler, IDisposable
{
    private readonly object _gate = new();
    private readonly AudioSessionControl _control;
    private readonly SimpleAudioVolume? _volume;
    private readonly AudioMeterInformation? _meter;
    private readonly ProcessIdentity _identity;
    private readonly Action<MixerSessionHandle> _changed;
    private bool _registered;    // mixer thread only
    private bool _disposed;      // guarded by _gate
    private float _volumeScalar; // guarded by _gate
    private bool _muted;         // guarded by _gate
    private bool _active;        // guarded by _gate
    private bool _disconnected;  // guarded by _gate
    private string? _displayName; // guarded by _gate

    private MixerSessionHandle(AudioSessionControl control, SimpleAudioVolume? volume, AudioMeterInformation? meter,
        string id, int processId, ProcessIdentity identity, Action<MixerSessionHandle> changed)
    {
        _control = control;
        _volume = volume;
        _meter = meter;
        _identity = identity;
        _changed = changed;
        Id = id;
        ProcessId = processId;

        _volumeScalar = Try(() => volume?.Volume ?? 0f, 0f);
        _muted = Try(() => volume?.Mute ?? false, false);
        _active = Try(() => control.State, AudioSessionState.AudioSessionStateInactive) == AudioSessionState.AudioSessionStateActive;
        _displayName = Try<string?>(() => control.DisplayName, null);
    }

    public string Id { get; }
    public int ProcessId { get; }
    public string ProcessName => _identity.ProcessName;
    public string? ExecutablePath => _identity.ExecutablePath;

    /// <summary>Smoothed peak. Mixer thread only.</summary>
    public double Peak { get; set; }

    // Mixer thread only: the display name the cached friendly name was built from.
    private string? _nameSource;
    private string? _name;

    public bool IsActive
    {
        get { lock (_gate) return _active; }
    }

    public bool IsDisconnected
    {
        get { lock (_gate) return _disconnected; }
    }

    /// <summary>
    /// Creates the handle and subscribes to its events. Takes ownership of <paramref name="control"/>: on failure it is
    /// disposed here, so the caller must not dispose it again.
    /// </summary>
    public static MixerSessionHandle Create(AudioSessionControl control, string id, int processId, Action<MixerSessionHandle> changed)
    {
        SimpleAudioVolume? volume = null;
        try
        {
            volume = Try<SimpleAudioVolume?>(() => control.SimpleAudioVolume, null);
            AudioMeterInformation? meter = Try<AudioMeterInformation?>(() => control.AudioMeterInformation, null);
            ProcessIdentity identity = ProcessIdentity.Resolve(processId);

            var handle = new MixerSessionHandle(control, volume, meter, id, processId, identity, changed);
            control.RegisterEventClient(handle);
            handle._registered = true;
            return handle;
        }
        catch (Exception)
        {
            volume?.Dispose();
            control.Dispose();
            throw;
        }
    }

    /// <summary>Current state as the UI sees it. Mixer thread only (it reads <see cref="Peak"/>).</summary>
    public AppAudioSession ToSession()
    {
        float scalar;
        bool muted;
        bool active;
        string? displayName;
        lock (_gate)
        {
            scalar = _volumeScalar;
            muted = _muted;
            active = _active;
            displayName = _displayName;
        }

        // The name only changes with the display name, so it is resolved once per change, not on every meter tick.
        if (!string.Equals(displayName, _nameSource, StringComparison.Ordinal) || _name is null)
        {
            _nameSource = displayName;
            _name = FriendlyAppName.Resolve(_identity.FileDescription, displayName, ProcessName);
        }

        return new AppAudioSession(
            Id,
            _name,
            ProcessId,
            ProcessName,
            AudioVolumeMath.FromScalar(scalar),
            muted,
            AudioVolumeMath.ClampPeak(Peak),
            active,
            ExecutablePath);
    }

    /// <summary>Reads the session meter. Mixer thread only. Returns 0 when the session has no meter or the read fails.</summary>
    public double ReadPeak()
    {
        if (_meter is null) return 0.0;
        try
        {
            return AudioVolumeMath.ClampPeak(_meter.MasterPeakValue);
        }
        catch (Exception)
        {
            return 0.0;
        }
    }

    /// <summary>Mixer thread only.</summary>
    public void SetVolume(int level0to100)
    {
        if (_volume is null) return;

        float scalar = AudioVolumeMath.ToScalar(level0to100);
        try
        {
            _volume.Volume = scalar;
        }
        catch (Exception)
        {
            return;   // the session went away between the lookup and the call
        }

        lock (_gate) _volumeScalar = scalar;
    }

    /// <summary>Mixer thread only.</summary>
    public void SetMuted(bool muted)
    {
        if (_volume is null) return;

        try
        {
            _volume.Mute = muted;
        }
        catch (Exception)
        {
            return;
        }

        lock (_gate) _muted = muted;
    }

    /// <summary>Mixer thread only. Unsubscribes, then releases the COM objects this handle owns.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        if (_registered)
        {
            try
            {
                _control.UnRegisterEventClient(this);
            }
            catch (Exception)
            {
                // The session is already gone; releasing the control below is still correct.
            }
        }

        _control.Dispose();
        _volume?.Dispose();
    }

    // ---- IAudioSessionEventsHandler (arbitrary Core Audio threads; must not throw) ----

    public void OnVolumeChanged(float volume, bool isMuted)
    {
        lock (_gate)
        {
            _volumeScalar = volume;
            _muted = isMuted;
        }
        Notify();
    }

    public void OnStateChanged(AudioSessionState newState)
    {
        lock (_gate) _active = newState == AudioSessionState.AudioSessionStateActive;
        Notify();
    }

    public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
    {
        lock (_gate)
        {
            _disconnected = true;
            _active = false;
        }
        Notify();
    }

    public void OnDisplayNameChanged(string displayName)
    {
        lock (_gate) _displayName = displayName;
        Notify();
    }

    public void OnIconPathChanged(string iconPath)
    {
        // The icon comes from the executable, not from the session.
    }

    public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex)
    {
        // Per-channel levels are not shown.
    }

    public void OnGroupingParamChanged(ref Guid groupingId)
    {
        // Grouping does not affect the list.
    }

    private void Notify()
    {
        bool disposed;
        lock (_gate) disposed = _disposed;
        if (disposed) return;

        try
        {
            _changed(this);
        }
        catch (Exception)
        {
            // A failing listener must not break the Core Audio callback thread.
        }
    }

    private static T Try<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
