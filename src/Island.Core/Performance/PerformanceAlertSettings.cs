namespace Island.Core.Performance;

/// <summary>Temperature alert switch and limits. Stored in performance.json, separate from IslandSettings.</summary>
public sealed record PerformanceAlertSettings(bool Enabled, int CpuLimitC, int GpuLimitC)
{
    public const int DefaultCpuLimitC = 85;
    public const int DefaultGpuLimitC = 83;
    public const int MinLimitC = 60;
    public const int MaxLimitC = 105;

    public static PerformanceAlertSettings Default { get; } = new(true, DefaultCpuLimitC, DefaultGpuLimitC);

    public static int ClampLimit(int celsius) => Math.Clamp(celsius, MinLimitC, MaxLimitC);

    /// <summary>Returns the settings with the CPU limit set, clamped to the allowed range.</summary>
    public PerformanceAlertSettings WithCpuLimit(int celsius) => this with { CpuLimitC = ClampLimit(celsius) };

    /// <summary>Returns the settings with the GPU limit set, clamped to the allowed range.</summary>
    public PerformanceAlertSettings WithGpuLimit(int celsius) => this with { GpuLimitC = ClampLimit(celsius) };

    /// <summary>A hand-edited file may hold any numbers: limits are brought into range.</summary>
    public PerformanceAlertSettings Sanitized() =>
        this with { CpuLimitC = ClampLimit(CpuLimitC), GpuLimitC = ClampLimit(GpuLimitC) };
}
