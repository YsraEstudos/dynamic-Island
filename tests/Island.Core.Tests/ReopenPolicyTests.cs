using Island.Core.Application;
using Island.Core.Models;

namespace Island.Core.Tests;

public class ReopenPolicyTests
{
    [Fact]
    public void Reopen_returns_to_the_clipboard_when_it_was_the_last_panel_and_is_enabled()
    {
        Assert.Equal(IslandMode.Clipboard, ReopenPolicy.Target(IslandMode.Clipboard, clipboardEnabled: true));
    }

    [Fact]
    public void Reopen_returns_to_the_shelf_when_the_shelf_was_the_last_panel()
    {
        Assert.Equal(IslandMode.Expanded, ReopenPolicy.Target(IslandMode.Expanded, clipboardEnabled: true));
        Assert.Equal(IslandMode.Expanded, ReopenPolicy.Target(IslandMode.Expanded, clipboardEnabled: false));
    }

    [Fact]
    public void Reopen_falls_back_to_the_shelf_when_the_clipboard_is_disabled()
    {
        Assert.Equal(IslandMode.Expanded, ReopenPolicy.Target(IslandMode.Clipboard, clipboardEnabled: false));
    }
}
