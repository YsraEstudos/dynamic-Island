using Island.Core.Performance;

namespace Island.Core.Tests.Performance;

public class PerformanceValuesTests
{
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(42.5, 42.5)]
    [InlineData(150, 100)]
    public void Percent_clamps_to_0_to_100(double raw, double expected)
    {
        Assert.Equal<double?>(expected, PerformanceValues.Percent(raw));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Percent_treats_non_finite_values_as_unknown(double raw)
    {
        Assert.Null(PerformanceValues.Percent(raw));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(126)]
    [InlineData(double.NaN)]
    public void Temperature_rejects_implausible_values(double raw)
    {
        Assert.Null(PerformanceValues.Temperature(raw));
    }

    [Fact]
    public void Temperature_keeps_a_plausible_value()
    {
        Assert.Equal<double?>(85.0, PerformanceValues.Temperature(85.0));
    }

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData(50.0, 0.5)]
    [InlineData(120.0, 1.0)]
    public void Fraction_is_0_to_1_and_empty_when_unknown(double? percent, double expected)
    {
        Assert.Equal(expected, PerformanceValues.Fraction(percent));
    }

    [Theory]
    [InlineData(null, MetricLevel.Neutral)]
    [InlineData(69.9, MetricLevel.Neutral)]
    [InlineData(70.0, MetricLevel.Warning)]
    [InlineData(89.9, MetricLevel.Warning)]
    [InlineData(90.0, MetricLevel.Danger)]
    public void Percent_levels_turn_amber_at_70_and_red_at_90(double? percent, MetricLevel expected)
    {
        Assert.Equal(expected, PerformanceValues.PercentLevel(percent));
    }

    [Theory]
    [InlineData(null, MetricLevel.Neutral)]
    [InlineData(69.0, MetricLevel.Neutral)]
    [InlineData(70.0, MetricLevel.Warning)]
    [InlineData(84.9, MetricLevel.Warning)]
    [InlineData(85.0, MetricLevel.Danger)]
    public void Temperature_levels_turn_amber_15_below_the_limit_and_red_at_it(double? celsius, MetricLevel expected)
    {
        Assert.Equal(expected, PerformanceValues.TemperatureLevel(celsius, 85));
    }
}
