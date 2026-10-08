using Island.Core.Configuration;

namespace Island.Core.Tests;

public class ShelfLayoutTests
{
    [Fact]
    public void Settings_without_row_indexes_load_as_one_row()
    {
        var rows = ShelfLayout.ToRows(new[] { "nowplaying", "pomodoro" }, Array.Empty<int>());

        Assert.Equal(new[] { new[] { "nowplaying", "pomodoro" } }, rows.Select(r => r.ToArray()));
    }

    [Fact]
    public void Rows_follow_their_index_and_keep_list_order_within_a_row()
    {
        var rows = ShelfLayout.ToRows(new[] { "a", "b", "c" }, new[] { 1, 0, 1 });

        Assert.Equal(new[] { new[] { "b" }, new[] { "a", "c" } }, rows.Select(r => r.ToArray()));
    }

    [Fact]
    public void Missing_indexes_default_to_row_zero_and_negative_indexes_clamp_to_zero()
    {
        var rows = ShelfLayout.ToRows(new[] { "a", "b", "c" }, new[] { 2, -4 });

        Assert.Equal(new[] { new[] { "b", "c" }, new[] { "a" } }, rows.Select(r => r.ToArray()));
    }

    [Fact]
    public void A_duplicated_id_keeps_only_its_first_occurrence()
    {
        var rows = ShelfLayout.ToRows(new[] { "a", "b", "a" }, new[] { 0, 1, 1 });

        Assert.Equal(new[] { new[] { "a" }, new[] { "b" } }, rows.Select(r => r.ToArray()));
    }

    [Fact]
    public void FromRows_round_trips_through_ToRows()
    {
        var rows = new List<List<string>> { new() { "a", "b" }, new() { "c" } };

        (IReadOnlyList<string> ids, IReadOnlyList<int> indexes) = ShelfLayout.FromRows(rows);
        var back = ShelfLayout.ToRows(ids, indexes);

        Assert.Equal(new[] { "a", "b", "c" }, ids);
        Assert.Equal(new[] { 0, 0, 1 }, indexes);
        Assert.Equal(rows.Select(r => r.ToArray()), back.Select(r => r.ToArray()));
    }

    [Fact]
    public void FromRows_drops_empty_rows_and_renumbers_the_rest()
    {
        var rows = new List<List<string>> { new(), new() { "a" }, new(), new() { "b" } };

        (IReadOnlyList<string> ids, IReadOnlyList<int> indexes) = ShelfLayout.FromRows(rows);

        Assert.Equal(new[] { "a", "b" }, ids);
        Assert.Equal(new[] { 0, 1 }, indexes);
    }

    [Fact]
    public void Settings_expose_their_rows()
    {
        var settings = new IslandSettings
        {
            ShelfWidgets = new[] { "pomodoro", "calendar", "filetray" },
            ShelfRows = new[] { 0, 1, 1 },
        };

        Assert.Equal(new[] { new[] { "pomodoro" }, new[] { "calendar", "filetray" } },
            settings.GetShelfRows().Select(r => r.ToArray()));
    }

    [Fact]
    public void Settings_with_different_row_indexes_are_not_equal()
    {
        var one = new IslandSettings { ShelfWidgets = new[] { "a", "b" }, ShelfRows = new[] { 0, 0 } };
        var two = new IslandSettings { ShelfWidgets = new[] { "a", "b" }, ShelfRows = new[] { 0, 1 } };

        Assert.NotEqual(one, two);
    }

    [Theory]
    [InlineData(0, 3, 0)]
    [InlineData(2, 3, 2)]
    [InlineData(3, 3, 0)]
    [InlineData(-1, 3, 0)]
    [InlineData(1, 1, 0)]
    public void ValidPage_keeps_a_row_that_still_exists_and_falls_back_to_the_first_row(int page, int rowCount, int expected)
    {
        Assert.Equal(expected, ShelfLayout.ValidPage(page, rowCount));
    }
}
