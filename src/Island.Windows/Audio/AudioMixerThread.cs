using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Audio;

/// <summary>
/// The one thread that touches the mixer's Core Audio objects. Work arrives as queued actions and runs in order. While
/// metering is on, the same loop also runs the meter tick about every <see cref="MeterIntervalMs"/>. With nothing queued
/// and metering off the thread blocks, so an idle mixer costs no CPU.
/// </summary>
internal sealed class AudioMixerThread
{
    public const int MeterIntervalMs = 40;

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly Action _meterTick;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ILogger? _logger;
    private int _started;
    private bool _meteringEnabled;   // worker thread only
    private long _nextMeterMs;       // worker thread only

    public AudioMixerThread(Action meterTick, ILogger? logger)
    {
        _meterTick = meterTick;
        _logger = logger;
        _thread = new Thread(Run) { IsBackground = true, Name = "Island.AudioMixer" };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    /// <summary>Only read or set from the worker thread (inside queued actions).</summary>
    public bool MeteringEnabled
    {
        get => _meteringEnabled;
        set
        {
            if (value && !_meteringEnabled) _nextMeterMs = _clock.ElapsedMilliseconds;
            _meteringEnabled = value;
        }
    }

    /// <summary>Starts the thread. Idempotent.</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0) _thread.Start();
    }

    /// <summary>Queues work. Returns false once the thread has been stopped.</summary>
    public bool Post(Action action)
    {
        try
        {
            return _queue.TryAdd(action);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Lets queued work finish, then ends the thread. Waits at most three seconds.</summary>
    public void Stop()
    {
        try
        {
            _queue.CompleteAdding();
        }
        catch (InvalidOperationException)
        {
            // Already stopped.
        }

        if (Volatile.Read(ref _started) == 1) _thread.Join(TimeSpan.FromSeconds(3));
    }

    private void Run()
    {
        while (!_queue.IsCompleted)
        {
            int wait = Timeout.Infinite;
            if (_meteringEnabled) wait = (int)Math.Max(0, _nextMeterMs - _clock.ElapsedMilliseconds);

            if (_queue.TryTake(out Action? action, wait)) Invoke(action);

            if (_meteringEnabled && _clock.ElapsedMilliseconds >= _nextMeterMs)
            {
                _nextMeterMs = _clock.ElapsedMilliseconds + MeterIntervalMs;
                Invoke(_meterTick);
            }
        }
    }

    /// <summary>One failing action must not stop the thread: the rest of the mixer keeps working.</summary>
    private void Invoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Audio mixer work failed.");
        }
    }
}
