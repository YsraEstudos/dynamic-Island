namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// State of the Sons de foco tool: which sounds are on and how loud, the general volume, play and pause, and the sleep
/// timer. It owns the mix and drives the output. It lives in the tool registry, so playback survives the shelf being
/// rebuilt or hidden, and the sleep timer ticks on its own timer rather than on the widget's.
/// </summary>
public sealed class FocusSoundsModel : IDisposable
{
    /// <summary>Fade-in and fade-out when playing starts or pauses, so the output does not click.</summary>
    public static readonly TimeSpan FadeTime = TimeSpan.FromMilliseconds(400);

    /// <summary>The sleep timer fades out over this last stretch before it stops.</summary>
    public static readonly TimeSpan SleepFadeTime = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan TickPeriod = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private readonly IFocusAudioOutput _output;
    private readonly TimeProvider _time;
    private readonly FocusMix _mix;
    private readonly SleepTimer _sleep;
    private ITimer? _ticker;
    private DateTimeOffset? _stopAt;
    private bool _playing;
    private bool _outputRunning;
    private bool _sleepFading;
    private long _shownSleepSeconds = -1;
    private float _volume = 1f;
    private bool _disposed;

    public FocusSoundsModel(IFocusAudioOutput output, TimeProvider time, uint seed)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(time);

        _output = output;
        _time = time;
        _mix = new FocusMix(output.SampleRate, seed);
        _sleep = new SleepTimer(time);
        _output.SetVolume(_volume);
    }

    /// <summary>Raised when any state changes. May come from a timer thread; subscribers marshal to the UI themselves.</summary>
    public event EventHandler? Changed;

    public bool IsPlaying
    {
        get
        {
            lock (_gate) return _playing;
        }
    }

    /// <summary>False when the audio device could not be opened.</summary>
    public bool IsAudioAvailable => _output.IsAvailable;

    /// <summary>Play needs at least one sound on.</summary>
    public bool CanPlay => _mix.AnySoundOn;

    public float Volume
    {
        get
        {
            lock (_gate) return _volume;
        }
    }

    /// <summary>Selected sleep duration in minutes, 0 when off.</summary>
    public int SleepMinutes
    {
        get
        {
            lock (_gate) return _sleep.Minutes;
        }
    }

    /// <summary>Time left on the sleep timer while playback runs; null otherwise.</summary>
    public TimeSpan? SleepRemaining
    {
        get
        {
            lock (_gate) return _sleep.Remaining();
        }
    }

    public bool IsOn(FocusSoundKind kind) => _mix.IsOn(kind);

    public float Level(FocusSoundKind kind) => _mix.Level(kind);

    /// <summary>Switches a sound on or off. Turning the last one off pauses playback.</summary>
    public void SetOn(FocusSoundKind kind, bool on)
    {
        lock (_gate)
        {
            if (_disposed) return;

            _mix.SetOn(kind, on);
            if (!on && _playing && !_mix.AnySoundOn) PauseLocked();
        }
        RaiseChanged();
    }

    public void SetLevel(FocusSoundKind kind, float level)
    {
        _mix.SetLevel(kind, level);
        RaiseChanged();
    }

    public void SetVolume(float volume)
    {
        lock (_gate)
        {
            if (_disposed) return;

            _volume = Math.Clamp(volume, 0f, 1f);
            _output.SetVolume(_volume);
        }
        RaiseChanged();
    }

    public void Play()
    {
        bool raise = false;
        lock (_gate)
        {
            if (_disposed || _playing || !_mix.AnySoundOn) return;

            if (!_outputRunning)
            {
                _output.Start(_mix);
                if (!_output.IsAvailable)
                {
                    raise = true;
                }
                else
                {
                    _outputRunning = true;
                }
            }

            if (_outputRunning)
            {
                _playing = true;
                _stopAt = null;
                _sleepFading = false;
                _mix.FadeTo(1f, FadeTime.TotalSeconds);
                _sleep.Arm();
                _shownSleepSeconds = -1;
                EnsureTicker();
                raise = true;
            }
        }

        if (raise) RaiseChanged();
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed || !_playing) return;
            PauseLocked();
        }
        RaiseChanged();
    }

    public void TogglePlay()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    /// <summary>Moves the sleep timer to its next choice: off, 15, 30, 60 minutes. A running countdown restarts with it.</summary>
    public void CycleSleepTimer()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _sleep.Select(SleepTimer.NextOption(_sleep.Minutes));
            if (_playing)
            {
                _sleep.Arm();
                if (_sleepFading)
                {
                    _sleepFading = false;
                    _mix.FadeTo(1f, FadeTime.TotalSeconds);
                }
                _shownSleepSeconds = -1;
                EnsureTicker();
            }
        }
        RaiseChanged();
    }

    /// <summary>Stops the output and the timers. The output is disposed with the model.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _playing = false;
            _ticker?.Dispose();
            _ticker = null;
            StopOutputLocked();
        }
        _output.Dispose();
    }

    private void PauseLocked()
    {
        _playing = false;
        _sleepFading = false;
        _sleep.Disarm();
        _shownSleepSeconds = -1;
        _mix.FadeTo(0f, FadeTime.TotalSeconds);
        _stopAt = _time.GetUtcNow() + FadeTime;
        EnsureTicker();
    }

    private bool NeedsTicks => _stopAt is not null || (_playing && _sleep.IsArmed);

    private void EnsureTicker()
    {
        if (_ticker is null && NeedsTicks)
        {
            _ticker = _time.CreateTimer(OnTimerTick, null, TickPeriod, TickPeriod);
        }
    }

    private void OnTimerTick(object? state) => Tick();

    /// <summary>Runs the sleep countdown and the fade that ends playback. The timer only exists while one of them is due.</summary>
    private void Tick()
    {
        bool raise = false;
        lock (_gate)
        {
            if (_disposed) return;

            DateTimeOffset now = _time.GetUtcNow();
            if (_playing && _sleep.Remaining() is { } left)
            {
                if (left <= TimeSpan.Zero)
                {
                    PauseLocked();
                    raise = true;
                }
                else
                {
                    if (!_sleepFading && left <= SleepFadeTime)
                    {
                        _sleepFading = true;
                        _mix.FadeTo(0f, left.TotalSeconds);
                    }

                    long seconds = (long)Math.Ceiling(left.TotalSeconds);
                    if (seconds != _shownSleepSeconds)
                    {
                        _shownSleepSeconds = seconds;
                        raise = true;
                    }
                }
            }

            if (_stopAt is { } stopAt && now >= stopAt)
            {
                _stopAt = null;
                StopOutputLocked();
                raise = true;
            }

            if (!NeedsTicks && _ticker is not null)
            {
                _ticker.Dispose();
                _ticker = null;
            }
        }

        if (raise) RaiseChanged();
    }

    private void StopOutputLocked()
    {
        if (!_outputRunning) return;

        _output.Stop();
        _outputRunning = false;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
