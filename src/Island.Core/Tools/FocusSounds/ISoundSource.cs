namespace Island.Core.Tools.FocusSounds;

/// <summary>
/// A mono sound generator. <see cref="Fill"/> runs on the audio thread, so it must not allocate or block: it writes the
/// next samples in place and keeps its state between calls.
/// </summary>
public interface ISoundSource
{
    /// <summary>Writes the next samples. Values stay inside about [-0.95, 0.95] so the mix has headroom.</summary>
    void Fill(Span<float> mono);

    /// <summary>Clears the filters and restarts the random sequence, so the sound starts over as it did when created.</summary>
    void Reset();
}
