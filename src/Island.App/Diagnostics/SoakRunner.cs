using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Island.Core.Application;
using Island.Core.Fakes;
using Island.Core.Models;
using Serilog;

namespace Island.App.Diagnostics;

/// <summary>
/// Soak test behind <c>--soak[=minutes]</c>. Drives the coordinator with a seeded, fast stream of fake media, volume and
/// coordinator events on the UI dispatcher, and logs process counters every 10 s. The verdict compares the end of the run
/// with a reference taken after a 60 s warm-up (or with the start, for runs shorter than 2 min).
/// </summary>
public sealed class SoakRunner : IDisposable
{
    private const int Seed = 42;
    private const int MaxHandleGrowth = 100;
    private const long MaxWorkingSetGrowthBytes = 50L * 1024 * 1024;
    private const double BytesPerMb = 1024.0 * 1024.0;

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ShortRun = TimeSpan.FromMinutes(2);

    private readonly IslandCoordinator _coordinator;
    private readonly FakeMediaService _media;
    private readonly FakeVolumeService _volume;
    private readonly DispatcherTimer _timer;
    private readonly Action _onFinished;
    private readonly TimeSpan _duration;
    private readonly TimeSpan _warmUp;
    private readonly Random _random = new(Seed);
    private readonly Stopwatch _clock = new();
    private readonly Process _process = Process.GetCurrentProcess();

    private Sample _baseline;
    private Sample? _reference;
    private TimeSpan _nextSample;
    private long _events;
    private int _notices;
    private bool _started;
    private bool _finished;
    private bool _disposed;

    public SoakRunner(IslandCoordinator coordinator, FakeMediaService media, FakeVolumeService volume,
        Dispatcher dispatcher, TimeSpan duration, Action onFinished)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(onFinished);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        _coordinator = coordinator;
        _media = media;
        _volume = volume;
        _duration = duration;
        _onFinished = onFinished;
        _warmUp = duration < ShortRun ? TimeSpan.Zero : WarmUp;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TickInterval };
        _timer.Tick += OnTick;
    }

    /// <summary>Writes the baseline line and starts injecting events. Call once, on the UI thread.</summary>
    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;

        // The normal demo playlist is not set up in soak mode, so the runner provides its own for Next/Previous/Tick.
        _media.SetPlaylist(
            new MediaInfo("Blue Hour", "Nova Coast", null, true, TimeSpan.FromSeconds(83), TimeSpan.FromSeconds(214), "soak"),
            new MediaInfo("Glass Tides", "Nova Coast", null, true, TimeSpan.Zero, TimeSpan.FromSeconds(187), "soak"),
            new MediaInfo("Paper Lanterns", "Harbor Lines", null, true, TimeSpan.Zero, TimeSpan.FromSeconds(241), "soak"));

        Log.Information($"SOAK START duration={(int)_duration.TotalSeconds}s warmup={(int)_warmUp.TotalSeconds}s seed={Seed}");
        _clock.Start();
        _baseline = TakeSample(0);
        Log.Information(Format(_baseline));
        if (_warmUp == TimeSpan.Zero) _reference = _baseline;
        _nextSample = SampleInterval;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_finished) return;

        Inject();

        TimeSpan elapsed = _clock.Elapsed;
        if (elapsed >= _nextSample)
        {
            Sample sample = TakeSample((int)elapsed.TotalSeconds);
            Log.Information(Format(sample));
            if (_reference is null && elapsed >= _warmUp) _reference = sample;
            _nextSample += SampleInterval;
        }

        if (elapsed >= _duration) Finish();
    }

    /// <summary>One progress tick plus one randomly chosen action per timer tick.</summary>
    private void Inject()
    {
        _media.Tick(TickInterval);
        _events++;

        switch (_random.Next(8))
        {
            case 0:
                _volume.SetLevel(_random.Next(0, 101));
                break;
            case 1:
                _volume.SetMuted(_random.Next(2) == 0);
                break;
            case 2:
                _ = _media.NextAsync();
                break;
            case 3:
                _ = _media.PreviousAsync();
                break;
            case 4:
                _ = _media.PlayPauseAsync();
                break;
            case 5:
                _notices++;
                _coordinator.Post(new IslandEvent.NoticeRaised(
                    new Notice($"Soak notice {_notices}", "Stability run", "timer")));
                break;
            case 6:
                _coordinator.Post(new IslandEvent.ExpandRequested());
                break;
            default:
                _coordinator.Post(new IslandEvent.CollapseRequested());
                break;
        }

        _events++;
    }

    private void Finish()
    {
        _finished = true;
        _timer.Stop();
        _clock.Stop();

        int t = (int)_clock.Elapsed.TotalSeconds;
        Sample end = TakeSample(t);
        Sample reference = _reference ?? _baseline;
        long wsDelta = end.WorkingSet - reference.WorkingSet;
        int handleDelta = end.Handles - reference.Handles;
        int threadDelta = end.Threads - reference.Threads;

        Log.Information($"SOAK SUMMARY t={t}s events={end.Events} reference=t{reference.T}s " +
                        $"ws_delta={SignedMb(wsDelta)}MB handles_delta={handleDelta:+#;-#;0} threads_delta={threadDelta:+#;-#;0}");

        var failures = new List<string>();
        if (handleDelta >= MaxHandleGrowth) failures.Add($"handles +{handleDelta} (limit {MaxHandleGrowth})");
        if (wsDelta >= MaxWorkingSetGrowthBytes) failures.Add($"working set +{Mb(wsDelta)}MB (limit 50MB)");

        Log.Information(failures.Count == 0
            ? "SOAK RESULT: PASS"
            : "SOAK RESULT: FAIL " + string.Join("; ", failures));

        _onFinished();
    }

    private Sample TakeSample(int t)
    {
        _process.Refresh();
        return new Sample(
            T: t,
            WorkingSet: _process.WorkingSet64,
            PrivateBytes: _process.PrivateMemorySize64,
            ManagedBytes: GC.GetTotalMemory(false),
            Gen0: GC.CollectionCount(0),
            Gen1: GC.CollectionCount(1),
            Gen2: GC.CollectionCount(2),
            Handles: _process.HandleCount,
            Threads: _process.Threads.Count,
            Events: _events);
    }

    private string Format(Sample s) =>
        $"SOAK t={s.T}s ws={Mb(s.WorkingSet)}MB privateMB={Mb(s.PrivateBytes)} managedMB={Mb(s.ManagedBytes)} " +
        $"gc0/1/2={s.Gen0}/{s.Gen1}/{s.Gen2} handles={s.Handles} threads={s.Threads} events={s.Events}";

    private static string Mb(long bytes) => (bytes / BytesPerMb).ToString("F1", CultureInfo.InvariantCulture);

    private static string SignedMb(long bytes) =>
        (bytes / BytesPerMb).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _timer.Tick -= OnTick;
        if (_started && !_finished)
        {
            Log.Information($"SOAK RESULT: ABORTED at t={(int)_clock.Elapsed.TotalSeconds}s (stopped before the end; no verdict)");
        }

        _process.Dispose();
    }

    private readonly record struct Sample(
        int T,
        long WorkingSet,
        long PrivateBytes,
        long ManagedBytes,
        int Gen0,
        int Gen1,
        int Gen2,
        int Handles,
        int Threads,
        long Events);
}
