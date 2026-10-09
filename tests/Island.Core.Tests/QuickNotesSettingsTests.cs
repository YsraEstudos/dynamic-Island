using Island.Core.Configuration;

namespace Island.Core.Tests;

public sealed class QuickNotesSettingsTests
{
    [Fact]
    public void Quick_notes_hotkey_is_enabled_by_default()
    {
        Assert.True(new IslandSettings().QuickNotesHotkeyEnabled);
    }

    [Fact]
    public void Quick_notes_hotkey_preference_participates_in_settings_equality()
    {
        var enabled = new IslandSettings { QuickNotesHotkeyEnabled = true };

        Assert.NotEqual(enabled, enabled with { QuickNotesHotkeyEnabled = false });
    }
}
