using Island.Core.Budgets;

namespace Island.Core.Tests.Budgets;

public sealed class PercentFormatterTests
{
    [Theory]
    [InlineData(18.5, "18,5%")]
    [InlineData(18, "18%")]
    [InlineData(18.666, "18,7%")]
    [InlineData(0, "0%")]
    [InlineData(100, "100%")]
    public void Format_uses_pt_BR_with_at_most_one_decimal(double value, string expected)
    {
        Assert.Equal(expected, PercentFormatter.Format(value));
    }

    [Theory]
    [InlineData("18,5", 18.5)]
    [InlineData("18.5", 18.5)]
    [InlineData("18,5%", 18.5)]
    [InlineData("  18 % ", 18)]
    [InlineData("100", 100)]
    public void TryParse_accepts_common_forms(string text, double expected)
    {
        Assert.True(PercentFormatter.TryParse(text, out double value));
        Assert.Equal(expected, value, 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("%")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("NaN")]
    public void TryParse_rejects_invalid_text(string? text)
    {
        Assert.False(PercentFormatter.TryParse(text, out _));
    }
}
