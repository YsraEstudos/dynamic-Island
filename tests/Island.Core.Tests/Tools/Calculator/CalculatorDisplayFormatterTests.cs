using Island.Core.Tools.Calculator;

namespace Island.Core.Tests.Tools.Calculator;

public sealed class CalculatorDisplayFormatterTests
{
    [Theory]
    [InlineData(1000d, "1.000")]
    [InlineData(1234567.5d, "1.234.567,5")]
    [InlineData(999999999999d, "999.999.999.999")]
    public void Thousands_are_grouped_with_dots_and_the_decimal_comma_is_pt_br(double value, string expected)
    {
        Assert.Equal(expected, CalculatorDisplayFormatter.FormatValue((decimal)value));
    }

    [Fact]
    public void Trailing_zeros_are_not_shown()
    {
        Assert.Equal("1,5", CalculatorDisplayFormatter.FormatValue(1.50m));
        Assert.Equal("2", CalculatorDisplayFormatter.FormatValue(2.000m));
    }

    [Fact]
    public void Zero_is_plain_zero_and_negatives_keep_the_minus_sign()
    {
        Assert.Equal("0", CalculatorDisplayFormatter.FormatValue(0m));
        Assert.Equal("0", CalculatorDisplayFormatter.FormatValue(-0m));
        Assert.Equal("-0,5", CalculatorDisplayFormatter.FormatValue(-0.5m));
        Assert.Equal("-1.234", CalculatorDisplayFormatter.FormatValue(-1234m));
    }

    [Fact]
    public void Values_below_a_millionth_use_scientific_notation()
    {
        Assert.Equal("1,2E-7", CalculatorDisplayFormatter.FormatValue(0.00000012m));
        Assert.Equal("-1,5E-8", CalculatorDisplayFormatter.FormatValue(-0.000000015m));
    }

    [Fact]
    public void A_millionth_itself_is_still_written_out()
    {
        Assert.Equal("0,000001", CalculatorDisplayFormatter.FormatValue(0.000001m));
    }

    [Fact]
    public void Scientific_mantissa_that_rounds_to_ten_carries_into_the_exponent()
    {
        Assert.Equal("1E-7", CalculatorDisplayFormatter.FormatValue(0.0000000999999999m));
    }

    [Theory]
    [InlineData("1234.5", "1.234,5")]
    [InlineData("2.", "2,")]
    [InlineData("0.", "0,")]
    [InlineData("-0.", "-0,")]
    [InlineData("0.50", "0,50")]
    [InlineData("-12", "-12")]
    [InlineData("1234567", "1.234.567")]
    [InlineData("-1234.0", "-1.234,0")]
    public void Entry_keeps_what_was_typed_after_the_comma(string entry, string expected)
    {
        Assert.Equal(expected, CalculatorDisplayFormatter.FormatEntry(entry));
    }

    [Fact]
    public void Font_stays_full_size_for_short_numbers()
    {
        Assert.Equal(22, CalculatorDisplayFormatter.FontSizeFor("123456789"));
        Assert.Equal(22, CalculatorDisplayFormatter.FontSizeFor("Erro"));
    }

    [Fact]
    public void Font_shrinks_as_the_number_grows_but_never_below_the_minimum()
    {
        double nine = CalculatorDisplayFormatter.FontSizeFor(new string('1', 9));
        double twelve = CalculatorDisplayFormatter.FontSizeFor(new string('1', 12));
        double eighteen = CalculatorDisplayFormatter.FontSizeFor(new string('1', 18));

        Assert.True(nine > twelve);
        Assert.True(twelve > eighteen);
        Assert.Equal(14, CalculatorDisplayFormatter.FontSizeFor(new string('1', 40)));
    }

    [Theory]
    [InlineData(CalculatorKey.Add, "+")]
    [InlineData(CalculatorKey.Subtract, "−")]
    [InlineData(CalculatorKey.Multiply, "×")]
    [InlineData(CalculatorKey.Divide, "÷")]
    public void Operators_have_their_display_symbols(CalculatorKey key, string symbol)
    {
        Assert.Equal(symbol, CalculatorDisplayFormatter.OperatorSymbol(key));
    }

    [Fact]
    public void Error_text_is_erro()
    {
        Assert.Equal("Erro", CalculatorDisplayFormatter.ErrorText);
    }
}
