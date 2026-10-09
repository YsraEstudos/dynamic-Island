namespace Island.Core.Audio;

/// <summary>Conversions between the 0-100 levels shown in the UI and the 0-1 scalars Core Audio uses.</summary>
public static class AudioVolumeMath
{
    public const int MinLevel = 0;
    public const int MaxLevel = 100;

    public static int ClampLevel(int level) => Math.Clamp(level, MinLevel, MaxLevel);

    public static float ToScalar(int level) => ClampLevel(level) / 100f;

    /// <summary>Rounds to the nearest level. NaN and infinities read as silence instead of throwing.</summary>
    public static int FromScalar(float scalar)
    {
        if (!float.IsFinite(scalar)) return MinLevel;
        return ClampLevel((int)Math.Round(scalar * 100d, MidpointRounding.AwayFromZero));
    }

    /// <summary>Clamps a peak meter reading into 0-1; NaN reads as silence.</summary>
    public static double ClampPeak(double peak) => double.IsFinite(peak) ? Math.Clamp(peak, 0.0, 1.0) : 0.0;
}
