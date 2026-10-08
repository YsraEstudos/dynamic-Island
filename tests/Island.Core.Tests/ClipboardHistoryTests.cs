using Island.Core.Clipboard;
using Island.Core.Configuration;

namespace Island.Core.Tests;

public class ClipboardHistoryTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ClipboardHistory History(int maxItems = 50) =>
        new(() => new IslandSettings { ClipboardMaxItems = maxItems });

    private static ClipboardItem Text(string text, int minute = 0) =>
        ClipboardItem.FromText(text, Epoch.AddMinutes(minute));

    private static ClipboardItem Image(params byte[] bytes) =>
        ClipboardItem.FromImage(bytes, Epoch);

    [Fact]
    public void Newest_item_is_first_and_the_snapshot_is_a_copy()
    {
        var history = History();
        history.Add(Text("first"));
        var snapshot = history.Items;
        history.Add(Text("second"));

        Assert.Equal(new[] { "first" }, snapshot.Select(i => i.Text));
        Assert.Equal(new[] { "second", "first" }, history.Items.Select(i => i.Text));
    }

    [Fact]
    public void Identical_text_moves_the_old_entry_to_the_top_as_a_new_item()
    {
        var history = History();
        var a = Text("a");
        history.Add(a);
        history.Add(Text("b"));
        var a2 = Text("a");
        history.Add(a2);

        Assert.Equal(2, history.Items.Count);
        Assert.Equal(new[] { "a", "b" }, history.Items.Select(i => i.Text));
        Assert.Equal(a2.Id, history.Items[0].Id);
        Assert.DoesNotContain(a.Id, history.Items.Select(i => i.Id));
    }

    [Fact]
    public void Identical_images_are_deduplicated_by_content_not_by_array_instance()
    {
        var history = History();
        history.Add(Image(1, 2, 3));
        history.Add(Image(4, 5));
        var same = Image(1, 2, 3);
        history.Add(same);

        Assert.Equal(2, history.Items.Count);
        Assert.Equal(same.Id, history.Items[0].Id);
        Assert.Equal(new byte[] { 4, 5 }, history.Items[1].ImageBytes);
    }

    [Fact]
    public void Same_text_with_a_different_kind_is_not_a_duplicate()
    {
        var history = History();
        history.Add(Text("x"));
        history.Add(new ClipboardItem(Guid.NewGuid(), ClipboardKind.Link, "x", null, Epoch));

        Assert.Equal(2, history.Items.Count);
    }

    [Fact]
    public void Trims_the_oldest_entries_beyond_the_limit()
    {
        var history = History(maxItems: 3);
        for (var i = 1; i <= 5; i++) history.Add(Text($"item {i}"));

        Assert.Equal(new[] { "item 5", "item 4", "item 3" }, history.Items.Select(i => i.Text));
    }

    [Fact]
    public void Limit_is_at_least_one()
    {
        var history = History(maxItems: 0);
        history.Add(Text("one"));
        history.Add(Text("two"));

        Assert.Equal(new[] { "two" }, history.Items.Select(i => i.Text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void Items_without_text_are_ignored(string? text)
    {
        var history = History();
        var changed = 0;
        history.Changed += () => changed++;

        history.Add(new ClipboardItem(Guid.NewGuid(), ClipboardKind.Text, text, null, Epoch));

        Assert.Empty(history.Items);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Images_without_bytes_are_ignored()
    {
        var history = History();
        history.Add(new ClipboardItem(Guid.NewGuid(), ClipboardKind.Image, null, null, Epoch));
        history.Add(new ClipboardItem(Guid.NewGuid(), ClipboardKind.Image, null, Array.Empty<byte>(), Epoch));

        Assert.Empty(history.Items);
    }

    [Fact]
    public void Remove_deletes_by_id_and_raises_only_when_something_was_removed()
    {
        var history = History();
        var item = Text("keep me");
        history.Add(item);
        history.Add(Text("other"));
        var changed = 0;
        history.Changed += () => changed++;

        history.Remove(Guid.NewGuid());
        Assert.Equal(0, changed);

        history.Remove(item.Id);
        Assert.Equal(1, changed);
        Assert.Equal(new[] { "other" }, history.Items.Select(i => i.Text));
    }

    [Fact]
    public void Clear_empties_the_history_and_raises_once()
    {
        var history = History();
        history.Add(Text("a"));
        history.Add(Text("b"));
        var changed = 0;
        history.Changed += () => changed++;

        history.Clear();
        history.Clear();

        Assert.Empty(history.Items);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Changed_is_raised_after_each_add()
    {
        var history = History();
        var changed = 0;
        history.Changed += () => changed++;

        history.Add(Text("a"));
        history.Add(Text("b"));

        Assert.Equal(2, changed);
    }

    [Theory]
    [InlineData("https://a.b/c", ClipboardKind.Link)]
    [InlineData("http://example.com", ClipboardKind.Link)]
    [InlineData("  https://a.b/c  ", ClipboardKind.Link)]
    [InlineData("HTTPS://EXAMPLE.COM", ClipboardKind.Link)]
    [InlineData("https://a b", ClipboardKind.Text)]
    [InlineData("ftp://a.b/c", ClipboardKind.Text)]
    [InlineData("#1219ED", ClipboardKind.Color)]
    [InlineData("#abc", ClipboardKind.Color)]
    [InlineData("#11223344", ClipboardKind.Color)]
    [InlineData("#12345", ClipboardKind.Text)]
    [InlineData("#gggggg", ClipboardKind.Text)]
    [InlineData("hello", ClipboardKind.Text)]
    [InlineData("", ClipboardKind.Text)]
    [InlineData(null, ClipboardKind.Text)]
    public void Classify_sorts_text_into_links_colors_and_plain_text(string? text, ClipboardKind expected)
    {
        Assert.Equal(expected, ClipboardHistory.Classify(text!));
    }

    [Fact]
    public void FromText_uses_the_classification()
    {
        Assert.Equal(ClipboardKind.Color, ClipboardItem.FromText("#abc").Kind);
        Assert.Equal(ClipboardKind.Link, ClipboardItem.FromText("https://a.b/c").Kind);
    }

    [Fact]
    public void Concurrent_adds_keep_the_limit_and_never_throw()
    {
        var history = History(maxItems: 10);

        Parallel.For(0, 8, t =>
        {
            for (var i = 0; i < 100; i++) history.Add(Text($"t{t}-{i}"));
        });

        Assert.Equal(10, history.Items.Count);
        Assert.Equal(history.Items.Count, history.Items.Select(i => i.Text).Distinct().Count());
    }
}
