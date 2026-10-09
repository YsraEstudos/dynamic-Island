using Island.Core.Performance;

namespace Island.Core.Tests.Performance;

public class RingBufferTests
{
    [Fact]
    public void Empty_buffer_has_no_items()
    {
        var buffer = new RingBuffer<int>(3);

        Assert.Empty(buffer.ToArray());
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void Partly_filled_buffer_returns_items_in_order()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);

        Assert.Equal(new[] { 1, 2 }, buffer.ToArray());
    }

    [Fact]
    public void Overflow_keeps_the_newest_items_oldest_first()
    {
        var buffer = new RingBuffer<int>(3);
        for (int i = 1; i <= 7; i++) buffer.Add(i);

        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { 5, 6, 7 }, buffer.ToArray());
    }

    [Fact]
    public void Capacity_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
    }

    [Fact]
    public void History_keeps_one_minute_and_preserves_unknown_gaps()
    {
        var history = new PerformanceHistory();
        int total = PerformanceHistory.Capacity + 6;
        for (int i = 0; i < total; i++)
        {
            history.Add(PerformanceSnapshot.Empty with { CpuPercent = i % 2 == 0 ? null : i });
        }

        double?[] cpu = history.Cpu();
        Assert.Equal(PerformanceHistory.Capacity, cpu.Length);
        Assert.Equal<double?>(total - 1, cpu[^1]);
        Assert.Null(cpu[^2]);
        Assert.Empty(history.Gpu().Where(v => v is not null));
    }
}
