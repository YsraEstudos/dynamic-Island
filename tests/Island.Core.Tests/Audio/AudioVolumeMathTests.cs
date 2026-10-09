using Island.Core.Audio;

namespace Island.Core.Tests.Audio;

public sealed class AudioVolumeMathTests
{
    [Theory]
    [InlineData(-50, 0)]
    [InlineData(0, 0)]
    [InlineData(55, 55)]
    [InlineData(100, 100)]
    [InlineData(500, 100)]
    public void ClampLevel_KeepsTheLevelInsideZeroToHundred(int input, int expected)
    {
        Assert.Equal(expected, AudioVolumeMath.ClampLevel(input));
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(50, 0.5f)]
    [InlineData(100, 1f)]
    [InlineData(250, 1f)]
    public void ToScalar_MapsLevelToZeroToOne(int level, float expected)
    {
        Assert.Equal(expected, AudioVolumeMath.ToScalar(level), 4);
    }

    [Theory]
    [InlineData(0.0f, 0)]
    [InlineData(0.504f, 50)]
    [InlineData(0.5051f, 51)]   // just above the half step rounds up
    [InlineData(1.0f, 100)]
    [InlineData(1.7f, 100)]
    [InlineData(-0.3f, 0)]
    public void FromScalar_RoundsAndClamps(float scalar, int expected)
    {
        Assert.Equal(expected, AudioVolumeMath.FromScalar(scalar));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void FromScalar_NonFiniteReadsAsSilence(float scalar)
    {
        Assert.Equal(0, AudioVolumeMath.FromScalar(scalar));
    }

    [Fact]
    public void ToScalar_then_FromScalar_round_trips_every_level()
    {
        for (int level = 0; level <= 100; level++)
        {
            Assert.Equal(level, AudioVolumeMath.FromScalar(AudioVolumeMath.ToScalar(level)));
        }
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.4, 0.4)]
    [InlineData(3.0, 1.0)]
    [InlineData(double.NaN, 0.0)]
    public void ClampPeak_KeepsPeakInsideZeroToOne(double input, double expected)
    {
        Assert.Equal(expected, AudioVolumeMath.ClampPeak(input), 6);
    }
}
