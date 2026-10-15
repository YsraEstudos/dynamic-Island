using Island.Core.Models;
using Island.Core.Tools.Calculator;

namespace Island.Core.Tests;

public sealed class AuditRegressionTests
{
    [Fact]
    public void Throwing_subscriber_does_not_block_other_subscribers_or_reentrant_states()
    {
        using var h = new CoordinatorHarness();
        h.Start();
        bool first = true;
        h.Coordinator.StateChanged += state =>
        {
            if (!first) return;
            first = false;
            h.Post(new IslandEvent.CollapseRequested());
            throw new InvalidOperationException("subscriber failure");
        };
        var received = new List<IslandMode>();
        h.Coordinator.StateChanged += state => received.Add(state.Mode);
        var error = Record.Exception(() => h.Volume.SetLevel(30));
        Assert.Null(error);
        Assert.Equal([IslandMode.Volume, IslandMode.Compact], received);
        Assert.Equal(IslandMode.Compact, h.State.Mode);
    }

    [Fact]
    public void Percent_and_sign_preserve_scientific_display_and_pending_operands()
    {
        var calc = new CalculatorEngine();
        Press(calc, "1/100000=");
        calc.Press(CalculatorKey.Percent);
        Assert.Equal("1E-7", calc.DisplayText);
        calc.Press(CalculatorKey.ToggleSign);
        Assert.Equal("-1E-7", calc.DisplayText);
        calc.Press(CalculatorKey.Clear);
        Press(calc, "200+10");
        calc.Press(CalculatorKey.Percent);
        calc.Press(CalculatorKey.Equals);
        Assert.Equal("220", calc.DisplayText);
    }

    private static void Press(CalculatorEngine calc, string keys)
    {
        foreach (char key in keys)
            calc.Press(key switch { '/' => CalculatorKey.Divide, '+' => CalculatorKey.Add,
                '=' => CalculatorKey.Equals, _ => (CalculatorKey)((int)CalculatorKey.D0 + key - '0') });
    }

    [Fact]
    public void A_digit_after_a_generated_tiny_operand_starts_a_new_entry()
    {
        var calc = new CalculatorEngine();
        Press(calc, "1/1000000000=");
        calc.Press(CalculatorKey.Percent);
        calc.Press(CalculatorKey.D5);
        Assert.Equal("5", calc.DisplayText);
    }
}
