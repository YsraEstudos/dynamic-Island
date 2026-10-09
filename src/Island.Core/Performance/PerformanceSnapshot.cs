namespace Island.Core.Performance;

/// <summary>
/// One reading of the machine. Every field is optional: a sensor that is missing or needs elevation is null,
/// and the widget shows a dash instead of failing. Percentages are 0 to 100; temperatures are in Celsius.
/// </summary>
public sealed record PerformanceSnapshot(
    double? CpuPercent,
    double? GpuPercent,
    double? CpuTempC,
    double? GpuTempC,
    ulong? RamUsedBytes,
    ulong? RamTotalBytes,
    ulong? VramUsedBytes,
    ulong? VramTotalBytes)
{
    public static PerformanceSnapshot Empty { get; } = new(null, null, null, null, null, null, null, null);
}
