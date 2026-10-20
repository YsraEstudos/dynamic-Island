using System.IO;
using System.Text;
using Island.Core.Pomodoro;

namespace Island.App.Audio;

/// <summary>Original short PCM cues, synthesized locally with gentle attacks and tails. No downloads or OS sound scheme.</summary>
public static class PomodoroSoundWave
{
    private const int SampleRate = 44100;
    private readonly record struct Note(double Start, double Frequency, double Gain, double Decay);

    public static byte[] Create(PomodoroSoundKind kind)
    {
        kind = PomodoroSoundSelection.Normalize(kind, PomodoroSoundKind.SoftBell);
        (double Duration, Note[] Notes) cue = kind switch
        {
            PomodoroSoundKind.Piano => (1.8, [new(0, 523.25, 0.32, 4), new(0.2, 659.25, 0.26, 4), new(0.4, 783.99, 0.24, 4)]),
            PomodoroSoundKind.Digital => (1.0, [new(0, 659.25, 0.30, 14), new(0.18, 880, 0.30, 14), new(0.36, 1046.5, 0.26, 14)]),
            PomodoroSoundKind.Coffee => (1.4, [new(0, 1396.91, 0.30, 7), new(0.23, 1760, 0.22, 8)]),
            _ => (2.0, [new(0, 783.99, 0.35, 3), new(0.3, 1174.66, 0.20, 3)]),
        };
        var (duration, notes) = cue;
        int samples = (int)(duration * SampleRate);
        using var stream = new MemoryStream(44 + samples * 2);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (int i = 0; i < samples; i++)
        {
            double time = i / (double)SampleRate;
            double value = 0;
            foreach (var note in notes)
            {
                double t = time - note.Start;
                if (t < 0) continue;
                double phase = 2 * Math.PI * note.Frequency * t;
                double timbre = kind switch
                {
                    PomodoroSoundKind.Piano => (Math.Sin(phase) + 0.32 * Math.Sin(2 * phase) + 0.12 * Math.Sin(3 * phase)) / 1.44,
                    PomodoroSoundKind.Coffee => (Math.Sin(phase) + 0.45 * Math.Sin(2.71 * phase)) / 1.45,
                    PomodoroSoundKind.Digital => Math.Sin(phase),
                    _ => (Math.Sin(phase) + 0.35 * Math.Sin(2.76 * phase) * Math.Exp(-3 * t)) / 1.35,
                };
                value += timbre * note.Gain * Math.Min(1, t / 0.008) * Math.Exp(-note.Decay * t);
            }
            double tail = Math.Clamp((duration - time - 1.0 / SampleRate) / 0.12, 0, 1);
            writer.Write((short)Math.Round(Math.Clamp(value * tail, -0.8, 0.8) * short.MaxValue));
        }
        return stream.ToArray();
    }
}
