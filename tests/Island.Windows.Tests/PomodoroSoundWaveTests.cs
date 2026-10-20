using Island.App.Audio;
using Island.Core.Pomodoro;
using NAudio.Wave;
using System.Media;

namespace Island.Windows.Tests;

public sealed class PomodoroSoundWaveTests
{
    [Fact]
    public void All_cues_decode_as_short_distinct_non_silent_waves_with_smooth_edges()
    {
        var fingerprints = new HashSet<string>();
        foreach (var kind in Enum.GetValues<PomodoroSoundKind>())
        {
            byte[] wave = PomodoroSoundWave.Create(kind);
            Assert.True(fingerprints.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(wave))));
            using var stream = new MemoryStream(wave);
            using var reader = new WaveFileReader(stream);
            Assert.Equal(44100, reader.WaveFormat.SampleRate);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
            Assert.Equal(1, reader.WaveFormat.Channels);
            Assert.InRange(reader.TotalTime.TotalSeconds, 0.8, 2.1);
            short[] samples = new short[reader.Length / 2];
            Buffer.BlockCopy(wave, 44, samples, 0, samples.Length * 2);
            Assert.Equal(0, samples[0]);
            Assert.Equal(0, samples[^1]);
            Assert.InRange(samples.Max(s => Math.Abs((int)s)), 1000, 20000);
            Assert.InRange(samples.Zip(samples.Skip(1), (a, b) => Math.Abs(a - b)).Max(), 1, 6000);
            using var player = new SoundPlayer(new MemoryStream(wave));
            player.Load(); // Exercise the real decoder without producing audio during automated tests.
            Assert.True(player.IsLoadCompleted);
            if (Environment.GetEnvironmentVariable("POMODORO_SOUND_PREVIEW_DIR") is { Length: > 0 } previewDir)
            {
                Directory.CreateDirectory(previewDir);
                File.WriteAllBytes(Path.Combine(previewDir, $"{kind}.wav"), wave);
            }
        }
    }
}
