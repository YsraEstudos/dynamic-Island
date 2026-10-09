namespace Island.Windows.Performance;

/// <summary>What one GPU source could read. Each field is null when that source cannot provide it.</summary>
internal sealed record GpuReading(double? Percent, double? TempC, ulong? VramUsedBytes, ulong? VramTotalBytes)
{
    public static GpuReading None { get; } = new(null, null, null, null);
}
