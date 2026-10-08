using Island.Core.Shelf;

namespace Island.Core.Tests;

public class FileTrayTests
{
    [Fact]
    public void A_batch_is_added_on_top_in_its_given_order()
    {
        var tray = new FileTray();
        tray.Add(new[] { "old.txt" });
        tray.Add(new[] { @"C:\a.txt", @"C:\b.txt" });

        Assert.Equal(new[] { @"C:\a.txt", @"C:\b.txt", "old.txt" }, tray.Paths);
    }

    [Fact]
    public void Paths_is_an_immutable_snapshot()
    {
        var tray = new FileTray();
        tray.Add(new[] { "one" });
        var snapshot = tray.Paths;

        tray.Add(new[] { "two" });

        Assert.Equal(new[] { "one" }, snapshot);
        Assert.Equal(new[] { "two", "one" }, tray.Paths);
    }

    [Fact]
    public void Re_adding_a_path_moves_it_to_the_top_with_no_duplicate_case_insensitively()
    {
        var tray = new FileTray();
        tray.Add(new[] { @"C:\docs\a.txt", @"C:\docs\b.txt" });

        tray.Add(new[] { @"c:\DOCS\A.TXT" });

        Assert.Equal(new[] { @"c:\DOCS\A.TXT", @"C:\docs\b.txt" }, tray.Paths);
    }

    [Fact]
    public void Duplicates_inside_one_batch_are_collapsed()
    {
        var tray = new FileTray();
        tray.Add(new[] { "x", "X", "y" });

        Assert.Equal(new[] { "x", "y" }, tray.Paths);
    }

    [Fact]
    public void Holds_at_most_24_newest_entries()
    {
        var tray = new FileTray();
        for (var i = 0; i < 30; i++) tray.Add(new[] { $"f{i}" });

        Assert.Equal(FileTray.MaxItems, tray.Paths.Count);
        Assert.Equal(24, tray.Paths.Count);
        Assert.Equal("f29", tray.Paths[0]);
        Assert.Equal("f6", tray.Paths[^1]);
    }

    [Fact]
    public void Blank_and_null_paths_are_ignored_without_raising()
    {
        var tray = new FileTray();
        var changed = 0;
        tray.Changed += () => changed++;

        tray.Add(new[] { "", "  ", null! });

        Assert.Empty(tray.Paths);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Remove_is_case_insensitive_and_raises_only_when_removed()
    {
        var tray = new FileTray();
        tray.Add(new[] { @"C:\a.txt", @"C:\b.txt" });
        var changed = 0;
        tray.Changed += () => changed++;

        tray.Remove(@"c:\A.txt");
        tray.Remove(@"C:\missing.txt");

        Assert.Equal(new[] { @"C:\b.txt" }, tray.Paths);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Clear_empties_the_tray_and_raises_once()
    {
        var tray = new FileTray();
        tray.Add(new[] { "a", "b" });
        var changed = 0;
        tray.Changed += () => changed++;

        tray.Clear();
        tray.Clear();

        Assert.Empty(tray.Paths);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Concurrent_adds_keep_the_limit_and_stay_free_of_duplicates()
    {
        var tray = new FileTray();

        Parallel.For(0, 8, t =>
        {
            for (var i = 0; i < 50; i++) tray.Add(new[] { $"file-{t}-{i}", $"shared-{i % 5}" });
        });

        Assert.True(tray.Paths.Count <= FileTray.MaxItems);
        Assert.Equal(tray.Paths.Count, tray.Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
