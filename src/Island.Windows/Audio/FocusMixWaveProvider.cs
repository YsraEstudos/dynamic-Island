using System.Runtime.InteropServices;
using Island.Core.Tools.FocusSounds;
using NAudio.Wave;

namespace Island.Windows.Audio;

/// <summary>
/// Adapts the mono focus mix to the device's interleaved float format: the same sample goes to every channel. It runs on
/// the audio thread, so the scratch buffer is reused and grows only when the device asks for a bigger block.
/// </summary>
internal sealed class FocusMixWaveProvider(FocusMix mix, WaveFormat format, Func<float> volume) : IWaveProvider
{
    private readonly int _channels = format.Channels;
    private float[] _mono = Array.Empty<float>();

    public WaveFormat WaveFormat { get; } = format;

    public int Read(byte[] buffer, int offset, int count)
    {
        int frameBytes = _channels * sizeof(float);
        int frames = count / frameBytes;

        if (_mono.Length < frames) _mono = new float[frames];
        Span<float> mono = _mono.AsSpan(0, frames);
        mix.MixInto(mono);

        Span<float> samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, frames * frameBytes));
        float level = volume();
        for (int frame = 0; frame < frames; frame++)
        {
            samples.Slice(frame * _channels, _channels).Fill(mono[frame] * level);
        }

        // A partial frame at the end of the block is silence, never stale data.
        buffer.AsSpan(offset + frames * frameBytes, count - frames * frameBytes).Clear();
        return count;
    }
}
