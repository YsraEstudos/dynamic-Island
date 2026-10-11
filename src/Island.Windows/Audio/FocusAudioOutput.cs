using System.Collections.Concurrent;
using Island.Core.Tools.FocusSounds;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Island.Windows.Audio;

/// <summary>
/// Plays the focus mix through the default output in WASAPI shared mode. Every device call runs on one MTA worker thread,
/// the same way the mixer keeps its COM objects, so the UI never waits on the device for longer than it takes to open it
/// and a timer thread can stop playback safely. A missing or refused device never throws: <see cref="IsAvailable"/> turns
/// false and nothing plays.
/// </summary>
public sealed class FocusAudioOutput : IFocusAudioOutput
{
    private const int LatencyMs = 100;
    private const int FallbackRate = 44100;
    private const int FallbackChannels = 2;

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly int _channels;
    private volatile float _volume = 1f;
    private volatile bool _available = true;
    private int _disposed;
    private WasapiOut? _device;   // worker thread only

    public FocusAudioOutput()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Island.FocusSounds" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();

        (int rate, int channels) = Call(ProbeFormat, (FallbackRate, FallbackChannels));
        SampleRate = rate;
        _channels = channels;
    }

    /// <summary>Rate of the default device, so the mix is generated at the rate the device plays.</summary>
    public int SampleRate { get; }

    public bool IsAvailable => _available;

    public void Start(FocusMix mix)
    {
        ArgumentNullException.ThrowIfNull(mix);
        RunAndWait(() => OpenDevice(mix));
    }

    public void Stop() => RunAndWait(CloseDevice);

    public void SetVolume(float volume) => _volume = Math.Clamp(volume, 0f, 1f);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        RunAndWait(CloseDevice);
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(3));
    }

    private void OpenDevice(FocusMix mix)
    {
        if (_device is not null) return;

        try
        {
            var device = new WasapiOut(AudioClientShareMode.Shared, LatencyMs);
            try
            {
                var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, _channels);
                device.Init(new FocusMixWaveProvider(mix, format, () => _volume));
                device.Play();
            }
            catch
            {
                device.Dispose();
                throw;
            }

            _device = device;
            _available = true;
        }
        catch (Exception)
        {
            // Device failures come in many COM shapes; any of them just means no audio output here.
            _available = false;
        }
    }

    private void CloseDevice()
    {
        if (_device is null) return;

        try
        {
            _device.Stop();
        }
        finally
        {
            _device.Dispose();
            _device = null;
        }
    }

    private static (int Rate, int Channels) ProbeFormat()
    {
        using var enumerator = new MMDeviceEnumerator();
        using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        using AudioClient client = device.AudioClient;
        WaveFormat mix = client.MixFormat;
        return (mix.SampleRate, mix.Channels);
    }

    private void Run()
    {
        foreach (Action action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    private void RunAndWait(Action work) => Call(() =>
    {
        work();
        return true;
    }, false);

    /// <summary>Runs work on the worker thread and waits for its result. Returns the fallback if the worker has stopped.</summary>
    private T Call<T>(Func<T> work, T fallback)
    {
        using var done = new ManualResetEventSlim(false);
        T result = fallback;
        try
        {
            _queue.Add(() =>
            {
                try
                {
                    result = work();
                }
                catch (Exception)
                {
                    // Keep the worker alive: a failed device call must not stop the next one.
                }
                finally
                {
                    done.Set();
                }
            });
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }

        done.Wait();
        return result;
    }
}
