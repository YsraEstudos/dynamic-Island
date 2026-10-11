using Island.Core.Clipboard;
using Island.Core.Tools.TextAnalyzer;

namespace Island.Core.Tests.Tools;

public class TextAnalyzerModelTests
{
    private static readonly string LongText = string.Join(' ', Enumerable.Repeat("palavra", TextAnalyzerModel.SyncLimit));

    [Fact]
    public void Latest_clipboard_text_skips_images_and_takes_the_newest_text()
    {
        var items = new[]
        {
            ClipboardItem.FromImage(new byte[] { 1, 2, 3 }),
            ClipboardItem.FromText("novo"),
            ClipboardItem.FromText("antigo"),
        };

        Assert.Equal("novo", TextAnalyzerModel.LatestClipboardText(items));
    }

    [Fact]
    public void Latest_clipboard_text_is_null_without_any_text()
    {
        Assert.Null(TextAnalyzerModel.LatestClipboardText(Array.Empty<ClipboardItem>()));
        Assert.Null(TextAnalyzerModel.LatestClipboardText(new[] { ClipboardItem.FromImage(new byte[] { 1 }) }));
    }

    [Fact]
    public void Short_text_is_analysed_at_once_and_raises_Changed_once()
    {
        using var model = new TextAnalyzerModel();
        int changes = 0;
        model.Changed += () => changes++;

        model.SetText("Olá, mundo!");

        TextAnalyzerModel.Snapshot state = model.State;
        Assert.Equal("Olá, mundo!", state.Text);
        Assert.Equal(2, state.Stats.Words);
        Assert.False(state.IsAnalyzing);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Setting_the_same_text_again_changes_nothing()
    {
        using var model = new TextAnalyzerModel();
        model.SetText("um dois");
        int changes = 0;
        model.Changed += () => changes++;

        model.SetText("um dois");

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Blank_text_clears_the_analysis()
    {
        using var model = new TextAnalyzerModel();
        model.SetText("um dois");

        model.SetText("   ");

        Assert.Null(model.State.Text);
        Assert.Equal(TextStats.Empty, model.State.Stats);
    }

    [Fact]
    public async Task Long_text_is_analysed_off_the_caller_thread_and_then_published()
    {
        using var model = new TextAnalyzerModel();
        bool analysingAtStart = false;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.Changed += () =>
        {
            if (model.State.IsAnalyzing) analysingAtStart = true;
            else if (model.State.Text is not null) done.TrySetResult();
        };

        model.SetText(LongText);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(analysingAtStart);
        Assert.Equal(TextAnalyzerModel.SyncLimit, model.State.Stats.Words);
        Assert.False(model.State.IsAnalyzing);
    }

    [Fact]
    public async Task A_newer_text_wins_over_a_long_analysis_still_running()
    {
        using var model = new TextAnalyzerModel();

        model.SetText(LongText);
        model.SetText("curto");
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        TextAnalyzerModel.Snapshot state = model.State;
        Assert.Equal("curto", state.Text);
        Assert.Equal(1, state.Stats.Words);
        Assert.False(state.IsAnalyzing);
    }
}
