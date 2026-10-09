using Island.Windows.Performance;

namespace Island.Windows.Tests.Performance;

/// <summary>Smoke tests against the real machine: they check that sampling never throws and that the always-available RAM reading works.</summary>
public sealed class WindowsPerformanceSamplerTests
{
    [Fact]
    public void Sample_reports_ram_and_never_throws_even_without_sensors()
    {
        using var sampler = new WindowsPerformanceSampler();

        // The first CPU read has no previous sample, so it is null by design.
        var first = sampler.Sample();
        var second = sampler.Sample();

        Assert.NotNull(second.RamTotalBytes);
        Assert.True(second.RamTotalBytes > 0);
        Assert.True(second.RamUsedBytes <= second.RamTotalBytes);
        Assert.Null(first.CpuPercent);
        if (second.CpuPercent is { } cpu) Assert.InRange(cpu, 0.0, 100.0);
        if (second.CpuTempC is { } temp) Assert.InRange(temp, 1.0, 125.0);
    }

    [Fact]
    public void Dispose_is_idempotent_and_samples_after_it_are_empty()
    {
        var sampler = new WindowsPerformanceSampler();
        sampler.Dispose();
        sampler.Dispose();

        Assert.Equal(Island.Core.Performance.PerformanceSnapshot.Empty, sampler.Sample());
    }
}
