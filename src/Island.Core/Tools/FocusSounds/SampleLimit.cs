namespace Island.Core.Tools.FocusSounds;

/// <summary>Keeps generator output inside the range the mix expects, with headroom left for summing several sounds.</summary>
internal static class SampleLimit
{
    public const float Ceiling = 0.95f;

    public static float Clamp(float sample) => Math.Clamp(sample, -Ceiling, Ceiling);
}
