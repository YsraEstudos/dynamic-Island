using Island.Core.Audio;

namespace Island.Core.Tests.Audio;

public sealed class PeakSmootherTests
{
    private const double Frame = 0.040;   // the meter is read about every 40 ms

    [Fact]
    public void Attack_rises_faster_than_release_falls()
    {
        double risen = PeakSmoother.Step(0.0, 1.0, Frame);
        double fallen = PeakSmoother.Step(1.0, 0.0, Frame);

        Assert.True(risen > 0.5, $"one 40 ms frame should reach most of a new peak, got {risen:F3}");
        Assert.True(fallen > 0.8, $"a falling peak should decay slowly, got {fallen:F3}");
        Assert.True(1.0 - fallen < risen, "decay in one frame must be smaller than the rise in one frame");
    }

    [Fact]
    public void Output_never_overshoots_the_target()
    {
        double value = 0.0;
        for (int i = 0; i < 200; i++)
        {
            value = PeakSmoother.Step(value, 0.6, Frame);
            Assert.InRange(value, 0.0, 0.6);
        }
    }

    [Fact]
    public void Converges_to_a_steady_target()
    {
        double value = 0.0;
        for (int i = 0; i < 100; i++) value = PeakSmoother.Step(value, 0.7, Frame);

        Assert.Equal(0.7, value, 3);
    }

    [Fact]
    public void Silence_is_snapped_to_exact_zero()
    {
        double value = 0.05;
        for (int i = 0; i < 200; i++) value = PeakSmoother.Step(value, 0.0, Frame);

        Assert.Equal(0.0, value);
    }

    [Fact]
    public void Zero_or_negative_elapsed_time_keeps_the_value()
    {
        Assert.Equal(0.4, PeakSmoother.Step(0.4, 1.0, 0.0));
        Assert.Equal(0.4, PeakSmoother.Step(0.4, 1.0, -1.0));
    }

    [Fact]
    public void A_long_gap_jumps_at_most_to_the_capped_step()
    {
        // A paused meter returns after minutes: the step is capped, so the value cannot jump past the target.
        double value = PeakSmoother.Step(0.0, 1.0, 600.0);

        Assert.InRange(value, 0.0, 1.0);
        Assert.Equal(PeakSmoother.Step(0.0, 1.0, PeakSmoother.MaxStepSeconds), value, 9);
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.005, false)]
    [InlineData(0.01, true)]
    [InlineData(0.9, true)]
    public void IsAudible_uses_the_threshold(double peak, bool expected)
    {
        Assert.Equal(expected, PeakSmoother.IsAudible(peak));
    }
}
