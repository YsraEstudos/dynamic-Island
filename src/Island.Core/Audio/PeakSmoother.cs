namespace Island.Core.Audio;

/// <summary>
/// Smooths a peak meter reading with a fast attack and a slower release, so bars rise on a beat and fall gently
/// instead of flickering. Each step is an exponential approach with a time constant, so the result does not depend
/// on how often the meter is read (only on the elapsed time).
/// </summary>
public static class PeakSmoother
{
    /// <summary>Below this the peak counts as silence. It is snapped to exactly 0, so idle sessions stop animating.</summary>
    public const double AudibleThreshold = 0.01;

    /// <summary>Time constant for a rising peak, in seconds.</summary>
    public const double AttackSeconds = 0.030;

    /// <summary>Time constant for a falling peak, in seconds.</summary>
    public const double ReleaseSeconds = 0.220;

    /// <summary>Longest step used for one update. A longer gap (the meter was paused) jumps to the target instead.</summary>
    public const double MaxStepSeconds = 0.25;

    /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> over <paramref name="seconds"/>.</summary>
    public static double Step(double current, double target, double seconds)
    {
        double from = AudioVolumeMath.ClampPeak(current);
        double to = AudioVolumeMath.ClampPeak(target);
        if (!double.IsFinite(seconds) || seconds <= 0.0) return from;

        double dt = Math.Min(seconds, MaxStepSeconds);
        double tau = to > from ? AttackSeconds : ReleaseSeconds;
        double alpha = 1.0 - Math.Exp(-dt / tau);
        double next = from + (to - from) * alpha;

        if (to < AudibleThreshold && next < AudibleThreshold) return 0.0;
        return AudioVolumeMath.ClampPeak(next);
    }

    public static bool IsAudible(double peak) => AudioVolumeMath.ClampPeak(peak) >= AudibleThreshold;
}
