using Island.Core.Performance;

namespace Island.Core.Tests.Performance;

public class GpuEngineUsageTests
{
    private static string Engine(int pid, int phys, string engine) =>
        $"pid_{pid}_luid_0x00000000_0x0000E4D2_phys_{phys}_eng_0_engtype_{engine}";

    [Fact]
    public void Busiest_is_null_without_usable_instances()
    {
        Assert.Null(GpuEngineUsage.Busiest(Array.Empty<(string, double)>()));
        Assert.Null(GpuEngineUsage.Busiest(new[] { ("not a gpu instance", 12.0) }));
        Assert.Null(GpuEngineUsage.Busiest(new[] { (Engine(1, 0, "3D"), double.NaN) }));
    }

    [Fact]
    public void Busiest_sums_processes_per_engine_and_caps_at_100()
    {
        var samples = new[]
        {
            (Engine(1, 0, "3D"), 60.0),
            (Engine(2, 0, "3D"), 70.0),
        };

        Assert.Equal<double?>(100.0, GpuEngineUsage.Busiest(samples));
    }

    [Fact]
    public void Busiest_takes_the_busiest_engine_not_the_sum_of_engines()
    {
        // Engines run in parallel: 80 on video decode and 10 on 3D is 80, not 90.
        var samples = new[]
        {
            (Engine(1, 0, "VideoDecode"), 80.0),
            (Engine(1, 0, "3D"), 10.0),
        };

        Assert.Equal<double?>(80.0, GpuEngineUsage.Busiest(samples));
    }

    [Fact]
    public void Busiest_picks_the_busiest_adapter()
    {
        var samples = new[]
        {
            (Engine(1, 0, "3D"), 30.0),
            (Engine(2, 0, "3D"), 20.0),
            (Engine(1, 1, "3D"), 75.0),
        };

        Assert.Equal<double?>(75.0, GpuEngineUsage.Busiest(samples));
    }

    [Fact]
    public void Busiest_skips_non_finite_values()
    {
        var samples = new[]
        {
            (Engine(1, 0, "3D"), double.NaN),
            (Engine(2, 0, "3D"), 25.0),
        };

        Assert.Equal<double?>(25.0, GpuEngineUsage.Busiest(samples));
    }

    [Fact]
    public void Dedicated_memory_is_null_without_instances()
    {
        Assert.Null(GpuEngineUsage.DedicatedBytes(Array.Empty<(string, double)>()));
    }

    [Fact]
    public void Dedicated_memory_is_the_busiest_adapter_total()
    {
        var samples = new[]
        {
            ("luid_0x00000000_0x0000E4D2_phys_0", 1_000_000_000.0),
            ("luid_0x00000000_0x0000E4D2_phys_0", 500_000_000.0),
            ("luid_0x00000000_0x0000E4D3_phys_1", 2_000_000_000.0),
            ("luid_0x00000000_0x0000E4D4_phys_2", -5.0),
        };

        Assert.Equal<ulong?>(2_000_000_000UL, GpuEngineUsage.DedicatedBytes(samples));
    }
}
