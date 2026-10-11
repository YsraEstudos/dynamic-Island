using Island.Core.Tools.FocusSounds.Generators;

namespace Island.Core.Tools.FocusSounds;

/// <summary>Builds the generator for a sound kind. The same kind and seed always produce the same sound.</summary>
public static class SoundSourceFactory
{
    public static ISoundSource Create(FocusSoundKind kind, int sampleRate, uint seed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);

        return kind switch
        {
            FocusSoundKind.Rain => new RainSource(sampleRate, seed),
            FocusSoundKind.WhiteNoise => new WhiteNoiseSource(seed),
            FocusSoundKind.PinkNoise => new PinkNoiseSource(seed),
            FocusSoundKind.BrownNoise => new BrownNoiseSource(seed),
            FocusSoundKind.Waves => new WavesSource(sampleRate, seed),
            FocusSoundKind.Fire => new FireSource(sampleRate, seed),
            FocusSoundKind.Wind => new WindSource(sampleRate, seed),
            FocusSoundKind.Cafe => new CafeSource(sampleRate, seed),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown focus sound."),
        };
    }
}
