using Island.Core.Budgets;

namespace Island.Core.Tests.Budgets;

public sealed class BudgetTests
{
    [Fact]
    public void Consumed_26_of_100_leaves_74_remaining()
    {
        var budget = new Budget { TotalPercent = 100, Mode = EntryMode.Consumed, EnteredValue = 26 };

        Assert.Equal(26, budget.Used, 6);
        Assert.Equal(74, budget.Remaining, 6);
    }

    [Fact]
    public void Remaining_74_of_100_means_26_used()
    {
        var budget = new Budget { TotalPercent = 100, Mode = EntryMode.Remaining, EnteredValue = 74 };

        Assert.Equal(26, budget.Used, 6);
        Assert.Equal(74, budget.Remaining, 6);
    }

    [Fact]
    public void Consumed_24_of_74_total_leaves_50_remaining()
    {
        var budget = new Budget { TotalPercent = 74, Mode = EntryMode.Consumed, EnteredValue = 24 };

        Assert.Equal(24, budget.Used, 6);
        Assert.Equal(50, budget.Remaining, 6);
    }

    [Fact]
    public void Consumed_above_total_is_capped_at_total()
    {
        var budget = new Budget { TotalPercent = 100, Mode = EntryMode.Consumed, EnteredValue = 150 };

        Assert.Equal(100, budget.Used, 6);
        Assert.Equal(0, budget.Remaining, 6);
    }

    [Fact]
    public void Negative_consumed_is_floored_at_zero()
    {
        var budget = new Budget { TotalPercent = 100, Mode = EntryMode.Consumed, EnteredValue = -5 };

        Assert.Equal(0, budget.Used, 6);
        Assert.Equal(100, budget.Remaining, 6);
    }

    [Fact]
    public void Remaining_above_total_is_capped_so_nothing_is_used()
    {
        var budget = new Budget { TotalPercent = 100, Mode = EntryMode.Remaining, EnteredValue = 150 };

        Assert.Equal(0, budget.Used, 6);
        Assert.Equal(100, budget.Remaining, 6);
    }
}
