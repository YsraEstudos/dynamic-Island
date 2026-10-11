using Island.Core.Tools.FocusSounds;

namespace Island.Core.Tests.Tools.FocusSounds;

/// <summary>A clock the test moves by hand. Timers it creates fire only when the test calls <see cref="FireTimers"/>.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly List<RecordingTimer> _timers = new();

    public DateTimeOffset Now { get; private set; } = start;

    public int LiveTimerCount => _timers.Count(timer => !timer.IsDisposed);

    public void Advance(TimeSpan by) => Now += by;

    public void FireTimers()
    {
        foreach (RecordingTimer timer in _timers.Where(timer => !timer.IsDisposed).ToList()) timer.Fire();
    }

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new RecordingTimer(callback, state);
        _timers.Add(timer);
        return timer;
    }

    private sealed class RecordingTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool IsDisposed { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => !IsDisposed;

        public void Dispose() => IsDisposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Output that records what the model asks for and can pull the mix as the device would.</summary>
internal sealed class RecordingOutput : IFocusAudioOutput
{
    private bool _failed;

    public int SampleRate => 44100;

    public bool IsAvailable => !_failed;

    /// <summary>Make the next Start fail the way a missing device does.</summary>
    public bool FailStart { get; set; }

    public bool Running { get; private set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public int DisposeCount { get; private set; }

    public float Volume { get; private set; } = -1f;

    public FocusMix? Mix { get; private set; }

    public void Start(FocusMix mix)
    {
        StartCount++;
        if (FailStart)
        {
            _failed = true;
            return;
        }

        Mix = mix;
        Running = true;
    }

    public void Stop()
    {
        StopCount++;
        Running = false;
    }

    public void SetVolume(float volume) => Volume = volume;

    public void Dispose() => DisposeCount++;

    /// <summary>Pulls <paramref name="frames"/> samples from the mix, as the device would while it runs.</summary>
    public float[] Pull(int frames)
    {
        var block = new float[frames];
        Mix!.MixInto(block);
        return block;
    }
}
