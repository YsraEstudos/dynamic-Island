using Island.Windows.Audio;

namespace Island.Windows.Tests;

/// <summary>The mixer's COM thread: queued work order, metering on and off, and survival of a failing action.</summary>
public sealed class AudioMixerThreadTests
{
    [Fact]
    public void Posted_work_runs_in_order_and_Stop_drains_the_queue()
    {
        var order = new List<int>();
        var thread = new AudioMixerThread(() => { }, logger: null);
        thread.Start();

        for (int i = 0; i < 50; i++)
        {
            int value = i;
            Assert.True(thread.Post(() => order.Add(value)));
        }
        thread.Stop();

        Assert.Equal(Enumerable.Range(0, 50), order);
    }

    [Fact]
    public void Metering_ticks_only_while_enabled()
    {
        int ticks = 0;
        var thread = new AudioMixerThread(() => Interlocked.Increment(ref ticks), logger: null);
        thread.Start();

        thread.Post(() => thread.MeteringEnabled = true);
        Assert.True(WaitUntil(() => Volatile.Read(ref ticks) >= 3, TimeSpan.FromSeconds(5)), "metering should tick while enabled");

        thread.Post(() => thread.MeteringEnabled = false);
        using var settled = new ManualResetEventSlim();
        thread.Post(() => settled.Set());
        Assert.True(settled.Wait(TimeSpan.FromSeconds(5)));

        int afterDisable = Volatile.Read(ref ticks);
        Thread.Sleep(200);
        thread.Stop();

        Assert.Equal(afterDisable, Volatile.Read(ref ticks));
    }

    [Fact]
    public void A_failing_action_does_not_stop_later_work()
    {
        bool ran = false;
        var thread = new AudioMixerThread(() => { }, logger: null);
        thread.Start();

        thread.Post(() => throw new InvalidOperationException("test failure"));
        thread.Post(() => ran = true);
        thread.Stop();

        Assert.True(ran);
    }

    [Fact]
    public void Post_after_Stop_is_rejected_and_Stop_is_repeatable()
    {
        var thread = new AudioMixerThread(() => { }, logger: null);
        thread.Start();
        thread.Stop();

        Assert.False(thread.Post(() => { }));
        thread.Stop();   // idempotent
    }

    [Fact]
    public void Stop_without_Start_does_not_throw()
    {
        var thread = new AudioMixerThread(() => { }, logger: null);

        thread.Stop();

        Assert.False(thread.Post(() => { }));
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(5);
        }
        return condition();
    }
}
