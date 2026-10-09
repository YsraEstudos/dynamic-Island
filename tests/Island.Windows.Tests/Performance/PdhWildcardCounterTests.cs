using Island.Windows.Performance;

namespace Island.Windows.Tests.Performance;

public sealed class PdhWildcardCounterTests
{
    [Fact]
    public void Collect_reads_every_instance_of_a_wildcard_counter()
    {
        using var counter = new PdhWildcardCounter(@"\Processor(*)\% Processor Time");

        // Rate counters need two samples: the first collect only primes them.
        counter.Collect();
        Thread.Sleep(200);
        var samples = counter.Collect();

        Assert.Contains(samples, sample => sample.Instance == "_Total");
        Assert.All(samples, sample => Assert.True(sample.Value >= 0.0));
    }

    [Fact]
    public void An_unknown_counter_path_is_reported_as_an_error()
    {
        Assert.ThrowsAny<Exception>(() => new PdhWildcardCounter(@"\No Such Object(*)\No Such Counter"));
    }
}
