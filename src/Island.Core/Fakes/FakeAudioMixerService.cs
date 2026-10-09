using System.Diagnostics;
using Island.Core.Abstractions;
using Island.Core.Audio;

namespace Island.Core.Fakes;

/// <summary>
/// In-memory <see cref="IAudioMixerService"/> for --demo: a few made-up apps. Peaks move on a timer only while a metering
/// scope is open, as the real service does, and the rest stay still. SetVolume and SetMuted raise <see cref="Changed"/>
/// synchronously on the calling thread.
/// </summary>
public sealed class FakeAudioMixerService : IAudioMixerService
{
    private const int MeterIntervalMs = 40;

    private readonly object _gate = new();
    private readonly List<DemoSession> _sessions = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Timer? _timer;
    private int _meterRefs;
    private double _lastSeconds;
    private bool _disposed;
    private IReadOnlyList<AppAudioSession> _snapshot = Array.Empty<AppAudioSession>();

    public FakeAudioMixerService()
    {
        _sessions.Add(new DemoSession("demo-discord", "Discord", 4101, "Discord", 70, false, true, 0.0, 9.0));
        _sessions.Add(new DemoSession("demo-spotify", "Spotify", 4202, "Spotify", 45, false, true, 1.3, 6.5));
        _sessions.Add(new DemoSession("demo-chrome", "Google Chrome", 4303, "chrome", 85, false, true, 2.1, 11.0));
        _sessions.Add(new DemoSession("demo-valorant", "VALORANT", 4404, "VALORANT-Win64-Shipping", 100, false, true, 3.7, 14.0));
        _sessions.Add(new DemoSession("demo-teams", "Microsoft Teams", 4505, "Teams", 60, false, false, 4.4, 4.0));
        _snapshot = BuildSnapshot();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<AppAudioSession> Sessions
    {
        get
        {
            lock (_gate) return _snapshot;
        }
    }

    /// <summary>Nothing to start: the demo sessions exist from construction.</summary>
    public void Start() { }

    public void SetVolume(string sessionId, int level0to100)
    {
        bool changed;
        lock (_gate)
        {
            DemoSession? session = Find(sessionId);
            changed = session is not null && session.Volume != AudioVolumeMath.ClampLevel(level0to100);
            if (session is not null) session.Volume = AudioVolumeMath.ClampLevel(level0to100);
            if (changed) _snapshot = BuildSnapshot();
        }

        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetMuted(string sessionId, bool muted)
    {
        bool changed;
        lock (_gate)
        {
            DemoSession? session = Find(sessionId);
            changed = session is not null && session.Muted != muted;
            if (session is not null) session.Muted = muted;
            if (changed) _snapshot = BuildSnapshot();
        }

        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public IDisposable BeginMetering()
    {
        lock (_gate)
        {
            if (_disposed) return new MeterScope(null);

            _meterRefs++;
            if (_timer is null)
            {
                _lastSeconds = _clock.Elapsed.TotalSeconds;
                _timer = new Timer(_ => Tick(), null, 0, MeterIntervalMs);
            }
        }

        return new MeterScope(this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Tick()
    {
        lock (_gate)
        {
            if (_timer is null) return;

            double now = _clock.Elapsed.TotalSeconds;
            double dt = now - _lastSeconds;
            _lastSeconds = now;
            foreach (DemoSession session in _sessions)
            {
                double raw = session.Active && !session.Muted ? session.RawPeak(now) : 0.0;
                session.Peak = PeakSmoother.Step(session.Peak, raw, dt);
            }
            _snapshot = BuildSnapshot();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ReleaseMetering()
    {
        bool stopped;
        lock (_gate)
        {
            if (_meterRefs > 0) _meterRefs--;
            stopped = _meterRefs == 0 && _timer is not null;
            if (!stopped) return;

            _timer!.Dispose();
            _timer = null;
            foreach (DemoSession session in _sessions) session.Peak = 0.0;
            _snapshot = BuildSnapshot();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private DemoSession? Find(string id) => _sessions.Find(s => s.Id == id);

    private IReadOnlyList<AppAudioSession> BuildSnapshot() =>
        _sessions.Select(s => new AppAudioSession(
            s.Id, s.Name, s.ProcessId, s.ProcessName, s.Volume, s.Muted, s.Peak, s.Active)).ToArray();

    private sealed class MeterScope(FakeAudioMixerService? owner) : IDisposable
    {
        private FakeAudioMixerService? _owner = owner;

        public void Dispose()
        {
            FakeAudioMixerService? owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseMetering();
        }
    }

    private sealed class DemoSession(string id, string name, int processId, string processName,
        int volume, bool muted, bool active, double phase, double speed)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public int ProcessId { get; } = processId;
        public string ProcessName { get; } = processName;
        public int Volume { get; set; } = volume;
        public bool Muted { get; set; } = muted;
        public bool Active { get; } = active;
        public double Peak { get; set; }

        /// <summary>Two superimposed sines, so the bar wanders instead of pulsing on a fixed beat.</summary>
        public double RawPeak(double seconds) =>
            AudioVolumeMath.ClampPeak(0.5 + 0.28 * Math.Sin(seconds * speed + phase) + 0.14 * Math.Sin(seconds * speed * 2.7 + phase * 2.0));
    }
}
