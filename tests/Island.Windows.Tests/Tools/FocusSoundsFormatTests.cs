using Island.App.Views.Widgets;
using Island.Core.Tools.FocusSounds;

namespace Island.Windows.Tests.Tools;

public sealed class FocusSoundsFormatTests
{
    [Theory]
    [InlineData(0f, "0%")]
    [InlineData(0.6f, "60%")]
    [InlineData(1f, "100%")]
    [InlineData(1.7f, "100%")]
    public void Percent_rounds_and_clamps_to_the_range(float level, string expected) =>
        Assert.Equal(expected, FocusSoundsFormat.Percent(level));

    [Fact]
    public void Sleep_label_shows_the_choice_while_idle_and_the_time_left_while_running()
    {
        Assert.Equal(FocusSoundsFormat.NoTimer, FocusSoundsFormat.SleepLabel(0, null));
        Assert.Equal("30 min", FocusSoundsFormat.SleepLabel(30, null));

        // Rounded up, so the label never shows 0 min while time is still left.
        Assert.Equal("15 min", FocusSoundsFormat.SleepLabel(15, TimeSpan.FromMinutes(14.5)));
        Assert.Equal("45 s", FocusSoundsFormat.SleepLabel(15, TimeSpan.FromSeconds(44.2)));
    }

    [Fact]
    public void Every_sound_has_a_name_for_its_chip()
    {
        foreach (FocusSoundKind kind in Enum.GetValues<FocusSoundKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(FocusSoundsFormat.SoundName(kind)));
        }
    }
}
