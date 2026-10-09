namespace Island.Core.Performance;

/// <summary>Neutral, amber or red. The widget maps it to the app's brushes.</summary>
public enum MetricLevel
{
    Neutral,
    Warning,
    Danger,
}

/// <summary>Normalisation and colour rules for raw sensor values. Pure, no I/O.</summary>
public static class PerformanceValues
{
    public const double MinPlausibleTempC = 1.0;
    public const double MaxPlausibleTempC = 125.0;
    public const double WarningPercent = 70.0;
    public const double DangerPercent = 90.0;
    /// <summary>A temperature turns amber this many degrees below its limit.</summary>
    public const double WarningMarginC = 15.0;

    /// <summary>Clamps to 0..100. NaN and infinities are unknown (null).</summary>
    public static double? Percent(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.0, 100.0) : null;

    /// <summary>Values outside a plausible sensor range are rejected: a broken ACPI zone often reports nonsense.</summary>
    public static double? Temperature(double value) =>
        double.IsFinite(value) && value >= MinPlausibleTempC && value <= MaxPlausibleTempC ? value : null;

    /// <summary>Bar fill for a percentage, 0 to 1. Unknown is empty.</summary>
    public static double Fraction(double? percent) =>
        percent is { } p ? Math.Clamp(p / 100.0, 0.0, 1.0) : 0.0;

    public static MetricLevel PercentLevel(double? percent)
    {
        if (percent is not { } p) return MetricLevel.Neutral;
        if (p >= DangerPercent) return MetricLevel.Danger;
        return p >= WarningPercent ? MetricLevel.Warning : MetricLevel.Neutral;
    }

    public static MetricLevel TemperatureLevel(double? celsius, int limitC)
    {
        if (celsius is not { } c) return MetricLevel.Neutral;
        if (c >= limitC) return MetricLevel.Danger;
        return c >= limitC - WarningMarginC ? MetricLevel.Warning : MetricLevel.Neutral;
    }
}
