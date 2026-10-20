using System.IO;
using System.Media;
using Island.Core.Pomodoro;
using Microsoft.Extensions.Logging;

namespace Island.App.Audio;

/// <summary>One shared player for transitions and previews; a new cue replaces the previous cue.</summary>
public sealed class PomodoroSoundPlayer(ILogger<PomodoroSoundPlayer> log) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<PomodoroSoundKind, byte[]> _waves = [];
    private SoundPlayer? _player;
    private MemoryStream? _stream;
    private bool _disposed;

    public bool Play(PomodoroSoundKind kind)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            try
            {
                StopLocked();
                kind = PomodoroSoundSelection.Normalize(kind, PomodoroSoundKind.SoftBell);
                if (!_waves.TryGetValue(kind, out byte[]? wave)) _waves[kind] = wave = PomodoroSoundWave.Create(kind);
                _stream = new MemoryStream(wave, writable: false);
                _player = new SoundPlayer(_stream);
                _player.Load();
                _player.Play();
                return true;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Pomodoro sound {Sound} could not play", kind);
                StopLocked();
                return false;
            }
        }
    }

    private void StopLocked()
    {
        try { _player?.Stop(); }
        finally
        {
            _player?.Dispose();
            _player = null;
            _stream?.Dispose();
            _stream = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopLocked();
            _waves.Clear();
        }
    }
}
