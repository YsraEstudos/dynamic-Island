using Island.Core.Tools.Calculator;

namespace Island.Core.Tests.Tools.Calculator;

public sealed class CalculatorEngineTests
{
    /// <summary>Presses one key per character: digits, ',' decimal point, + - * / operators, = % ~(sign) &lt;(backspace) C.</summary>
    private static CalculatorOutcome Type(CalculatorEngine engine, string keys)
    {
        CalculatorOutcome last = CalculatorOutcome.None;
        foreach (char c in keys) last = engine.Press(KeyFor(c));
        return last;
    }

    private static CalculatorKey KeyFor(char c) => c switch
    {
        >= '0' and <= '9' => (CalculatorKey)((int)CalculatorKey.D0 + (c - '0')),
        ',' => CalculatorKey.DecimalPoint,
        '+' => CalculatorKey.Add,
        '-' => CalculatorKey.Subtract,
        '*' => CalculatorKey.Multiply,
        '/' => CalculatorKey.Divide,
        '=' => CalculatorKey.Equals,
        '%' => CalculatorKey.Percent,
        '~' => CalculatorKey.ToggleSign,
        '<' => CalculatorKey.Backspace,
        'C' => CalculatorKey.Clear,
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "Unknown key."),
    };

    [Fact]
    public void Digits_build_a_number_and_a_leading_zero_is_replaced()
    {
        var engine = new CalculatorEngine();

        Type(engine, "0005");

        Assert.Equal("5", engine.DisplayText);
    }

    [Fact]
    public void Decimal_point_keeps_typed_zeros_and_ignores_a_second_comma()
    {
        var engine = new CalculatorEngine();

        Type(engine, "1,50");
        Assert.Equal("1,50", engine.DisplayText);
        Assert.Equal(CalculatorOutcome.None, engine.Press(CalculatorKey.DecimalPoint));
        Assert.Equal("1,50", engine.DisplayText);
    }

    [Fact]
    public void Comma_on_an_empty_display_starts_zero_comma()
    {
        var engine = new CalculatorEngine();

        Type(engine, ",");

        Assert.Equal("0,", engine.DisplayText);
    }

    [Fact]
    public void Operations_run_left_to_right_like_a_pocket_calculator()
    {
        var engine = new CalculatorEngine();

        Type(engine, "2+3*4=");

        Assert.Equal("20", engine.DisplayText);
    }

    [Fact]
    public void Operator_after_an_operation_shows_the_intermediate_result()
    {
        var engine = new CalculatorEngine();
        Type(engine, "2+3");

        CalculatorOutcome outcome = engine.Press(CalculatorKey.Multiply);

        Assert.Equal(CalculatorOutcome.Result, outcome);
        Assert.Equal("5", engine.DisplayText);
        Assert.Equal("5 ×", engine.Expression);
    }

    [Fact]
    public void Equals_repeats_the_last_operation()
    {
        var engine = new CalculatorEngine();

        Type(engine, "2+3=");
        Assert.Equal("5", engine.DisplayText);

        Type(engine, "=");
        Assert.Equal("8", engine.DisplayText);

        Type(engine, "=");
        Assert.Equal("11", engine.DisplayText);
    }

    [Fact]
    public void Equals_repeats_multiplication_with_the_same_second_operand()
    {
        var engine = new CalculatorEngine();

        Type(engine, "2*3==");

        Assert.Equal("18", engine.DisplayText);
    }

    [Fact]
    public void Repeated_equals_applies_to_a_new_number_typed_after_a_result()
    {
        var engine = new CalculatorEngine();
        Type(engine, "2+3=");

        Type(engine, "7=");

        Assert.Equal("10", engine.DisplayText);
    }

    [Fact]
    public void Equals_with_nothing_to_repeat_does_nothing()
    {
        var engine = new CalculatorEngine();

        Assert.Equal(CalculatorOutcome.None, engine.Press(CalculatorKey.Equals));
        Assert.Equal("0", engine.DisplayText);
    }

    [Fact]
    public void Equals_after_an_operator_uses_the_shown_value_as_second_operand()
    {
        var engine = new CalculatorEngine();

        Type(engine, "5+=");

        Assert.Equal("10", engine.DisplayText);
    }

    [Fact]
    public void Pressing_another_operator_before_the_second_number_only_replaces_it()
    {
        var engine = new CalculatorEngine();
        Type(engine, "2+");

        Assert.Equal(CalculatorOutcome.None, engine.Press(CalculatorKey.Multiply));
        Assert.Equal(CalculatorKey.Multiply, engine.ActiveOperator);
        Type(engine, "3=");

        Assert.Equal("6", engine.DisplayText);
    }

    [Fact]
    public void Subtraction_can_go_negative()
    {
        var engine = new CalculatorEngine();

        Type(engine, "2-5=");

        Assert.Equal("-3", engine.DisplayText);
    }

    [Fact]
    public void Division_by_zero_shows_error_and_is_flagged()
    {
        var engine = new CalculatorEngine();

        CalculatorOutcome outcome = Type(engine, "5/0=");

        Assert.Equal(CalculatorOutcome.Error, outcome);
        Assert.True(engine.IsError);
        Assert.Equal("Erro", engine.DisplayText);
        Assert.Equal(string.Empty, engine.Expression);
    }

    [Fact]
    public void In_error_operators_and_equals_are_ignored()
    {
        var engine = new CalculatorEngine();
        Type(engine, "5/0=");

        Assert.Equal(CalculatorOutcome.None, Type(engine, "+="));

        Assert.True(engine.IsError);
        Assert.Equal("Erro", engine.DisplayText);
    }

    [Fact]
    public void A_digit_clears_the_error_and_starts_a_new_number()
    {
        var engine = new CalculatorEngine();
        Type(engine, "5/0=");

        CalculatorOutcome outcome = engine.Press(CalculatorKey.D7);

        Assert.Equal(CalculatorOutcome.Entry, outcome);
        Assert.False(engine.IsError);
        Assert.Equal("7", engine.DisplayText);
    }

    [Fact]
    public void C_clears_the_error_and_the_whole_state()
    {
        var engine = new CalculatorEngine();
        Type(engine, "5/0=");

        Assert.Equal(CalculatorOutcome.Cleared, engine.Press(CalculatorKey.Clear));

        Assert.False(engine.IsError);
        Assert.Equal("0", engine.DisplayText);
        Assert.Equal(CalculatorOutcome.None, engine.Press(CalculatorKey.Equals));
    }

    [Fact]
    public void Percent_of_the_second_operand_when_adding_takes_a_share_of_the_first()
    {
        var engine = new CalculatorEngine();

        Type(engine, "200+10%=");

        Assert.Equal("220", engine.DisplayText);
    }

    [Fact]
    public void Percent_when_multiplying_just_divides_the_operand_by_100()
    {
        var engine = new CalculatorEngine();

        Type(engine, "200*10%=");

        Assert.Equal("20", engine.DisplayText);
    }

    [Fact]
    public void Percent_alone_divides_the_shown_value_by_100()
    {
        var engine = new CalculatorEngine();

        Type(engine, "50%");

        Assert.Equal("0,5", engine.DisplayText);
    }

    [Fact]
    public void Sign_toggles_the_number_being_typed()
    {
        var engine = new CalculatorEngine();

        Type(engine, "5~");
        Assert.Equal("-5", engine.DisplayText);

        Type(engine, "~");
        Assert.Equal("5", engine.DisplayText);
    }

    [Fact]
    public void Sign_with_nothing_typed_negates_the_shown_result()
    {
        var engine = new CalculatorEngine();
        Type(engine, "2+3=");

        Type(engine, "~");

        Assert.Equal("-5", engine.DisplayText);
    }

    [Fact]
    public void Sign_after_an_operator_negates_the_operand_not_the_left_side()
    {
        var engine = new CalculatorEngine();

        Type(engine, "2+~");

        Assert.Equal("-2", engine.DisplayText);
        Assert.Equal("2 + -2", engine.Expression);
    }

    [Fact]
    public void Sign_on_a_typed_zero_then_comma_reads_minus_zero_comma()
    {
        var engine = new CalculatorEngine();

        Type(engine, "0~,");

        Assert.Equal("-0,", engine.DisplayText);
    }

    [Fact]
    public void Backspace_removes_the_last_digit()
    {
        var engine = new CalculatorEngine();

        Type(engine, "123<");

        Assert.Equal("12", engine.DisplayText);
    }

    [Fact]
    public void Backspace_on_the_last_digit_leaves_zero()
    {
        var engine = new CalculatorEngine();

        Type(engine, "5<");

        Assert.Equal("0", engine.DisplayText);
    }

    [Fact]
    public void Backspace_removes_a_decimal_point_and_its_position()
    {
        var engine = new CalculatorEngine();

        Type(engine, "1,5<");

        Assert.Equal("1,", engine.DisplayText);
    }

    [Fact]
    public void Backspace_does_nothing_to_a_result()
    {
        var engine = new CalculatorEngine();
        Type(engine, "2+3=");

        Assert.Equal(CalculatorOutcome.None, engine.Press(CalculatorKey.Backspace));
        Assert.Equal("5", engine.DisplayText);
    }

    [Fact]
    public void Typing_stops_at_twelve_digits()
    {
        var engine = new CalculatorEngine();

        Type(engine, "123456789012");
        CalculatorOutcome outcome = engine.Press(CalculatorKey.D3);

        Assert.Equal(CalculatorOutcome.None, outcome);
        Assert.Equal("123.456.789.012", engine.DisplayText);
    }

    [Fact]
    public void Decimal_digits_count_towards_the_twelve_digit_limit()
    {
        var engine = new CalculatorEngine();

        Type(engine, "1,23456789012");

        Assert.Equal(CalculatorOutcome.None, engine.Press(CalculatorKey.D3));
        Assert.Equal("1,23456789012", engine.DisplayText);
    }

    [Fact]
    public void A_result_is_rounded_to_twelve_significant_digits()
    {
        var engine = new CalculatorEngine();

        Type(engine, "1/3=");

        Assert.Equal("0,333333333333", engine.DisplayText);
    }

    [Fact]
    public void A_result_at_a_trillion_is_an_overflow_error()
    {
        var engine = new CalculatorEngine();

        CalculatorOutcome outcome = Type(engine, "999999999999+1=");

        Assert.Equal(CalculatorOutcome.Error, outcome);
        Assert.Equal("Erro", engine.DisplayText);
    }

    [Fact]
    public void A_product_too_large_for_the_display_is_an_overflow_error()
    {
        var engine = new CalculatorEngine();

        Assert.Equal(CalculatorOutcome.Error, Type(engine, "999999999999*9="));
    }

    [Fact]
    public void A_very_small_result_is_kept_and_shown_in_scientific_notation()
    {
        var engine = new CalculatorEngine();

        Type(engine, "1/10000000=");

        Assert.Equal("1E-7", engine.DisplayText);
    }

    [Fact]
    public void Decimal_arithmetic_is_exact_for_money_style_values()
    {
        var engine = new CalculatorEngine();

        Type(engine, "0,1+0,2=");

        Assert.Equal("0,3", engine.DisplayText);
    }

    [Fact]
    public void Active_operator_is_highlighted_until_the_next_number_starts()
    {
        var engine = new CalculatorEngine();

        Type(engine, "2+");
        Assert.Equal(CalculatorKey.Add, engine.ActiveOperator);

        Type(engine, "3");
        Assert.Null(engine.ActiveOperator);
    }

    [Fact]
    public void Active_operator_is_cleared_by_equals()
    {
        var engine = new CalculatorEngine();

        Type(engine, "2+3=");

        Assert.Null(engine.ActiveOperator);
    }

    [Fact]
    public void Expression_shows_the_calculation_in_progress()
    {
        var engine = new CalculatorEngine();

        Type(engine, "12*3");

        Assert.Equal("12 × 3", engine.Expression);
    }

    [Fact]
    public void Expression_is_empty_once_the_result_is_shown()
    {
        var engine = new CalculatorEngine();

        Type(engine, "12*3=");

        Assert.Equal(string.Empty, engine.Expression);
    }

    [Fact]
    public void Clear_forgets_the_repeated_operation()
    {
        var engine = new CalculatorEngine();
        Type(engine, "2+3=");

        Type(engine, "C=");

        Assert.Equal("0", engine.DisplayText);
    }

    [Fact]
    public void Outcomes_tell_the_widget_what_moved()
    {
        var engine = new CalculatorEngine();

        Assert.Equal(CalculatorOutcome.Entry, engine.Press(CalculatorKey.D2));
        Assert.Equal(CalculatorOutcome.Entry, engine.Press(CalculatorKey.Add));
        Assert.Equal(CalculatorOutcome.Entry, engine.Press(CalculatorKey.D3));
        Assert.Equal(CalculatorOutcome.Result, engine.Press(CalculatorKey.Equals));
        Assert.Equal(CalculatorOutcome.Cleared, engine.Press(CalculatorKey.Clear));
    }
}
