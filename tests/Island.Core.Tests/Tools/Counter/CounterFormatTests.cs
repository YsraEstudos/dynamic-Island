using Island.Core.Tools.Counter;

namespace Island.Core.Tests.Tools.Counter;

public sealed class CounterFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(-42, "-42")]
    [InlineData(999_999, "999999")]
    public void Value_text_is_plain_digits(int value, string expected)
    {
        Assert.Equal(expected, CounterFormat.ValueText(value));
    }

    [Theory]
    [InlineData(1, "+1")]
    [InlineData(5, "+5")]
    [InlineData(10, "+10")]
    public void Step_text_carries_the_sign(int step, string expected)
    {
        Assert.Equal(expected, CounterFormat.StepText(step));
    }

    [Fact]
    public void Goal_suffix_is_empty_without_a_goal()
    {
        Assert.Equal(string.Empty, CounterFormat.GoalSuffix(0));
    }

    [Fact]
    public void Goal_suffix_shows_the_goal()
    {
        Assert.Equal("/ 20", CounterFormat.GoalSuffix(20));
    }
}
