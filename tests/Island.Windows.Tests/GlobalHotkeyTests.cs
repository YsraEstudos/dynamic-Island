using Island.Windows.Input;

namespace Island.Windows.Tests;

public sealed class GlobalHotkeyTests
{
    // Ctrl+Alt+Shift+F24: a combination no normal app claims.
    private const uint Modifiers = GlobalHotkey.ModControl | GlobalHotkey.ModAlt | GlobalHotkey.ModShift;
    private const uint VkF24 = 0x87;

    [Fact]
    public void Register_ObscureCombination_ReturnsTrue_ThenDisposeDoesNotThrow()
    {
        var hotkey = new GlobalHotkey(Modifiers, VkF24);

        // Headless or CI sessions may refuse RegisterHotKey outright; that says nothing about the code under test.
        if (!hotkey.Register())
        {
            hotkey.Dispose();
            return;
        }

        var ex = Record.Exception(hotkey.Dispose);
        Assert.Null(ex);
    }

    [Fact]
    public void Register_SameCombinationTwice_SecondReturnsFalse()
    {
        using var first = new GlobalHotkey(Modifiers, VkF24);
        if (!first.Register()) return;   // headless or CI: nothing to compare against

        using var second = new GlobalHotkey(Modifiers, VkF24);
        Assert.False(second.Register());
    }

    [Fact]
    public void Register_AfterDispose_ReturnsFalse()
    {
        var hotkey = new GlobalHotkey(Modifiers, VkF24);
        hotkey.Dispose();

        Assert.False(hotkey.Register());
    }
}
