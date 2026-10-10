using Island.App.Animations;
using Island.Core.Configuration;
using Island.Core.Models;

namespace Island.Windows.Tests;

public class IdleShapeTableTests
{
    [Fact]
    public void Idle_content_widens_the_compact_capsule_to_the_idle_width()
    {
        var settings = new IslandSettings();

        ShapeSize shape = IslandShapeTable.For(IslandMode.Compact, settings, false, false, [], idleShown: true);

        Assert.Equal(IslandShapeTable.IdleCompactWidth, shape.Width);
        Assert.Equal(settings.CompactHeight, shape.Height);
    }

    [Fact]
    public void Without_idle_content_the_capsule_keeps_its_configured_width()
    {
        var settings = new IslandSettings { CompactWidth = 124 };

        ShapeSize shape = IslandShapeTable.For(IslandMode.Compact, settings, false, false, [], idleShown: false);

        Assert.Equal(124, shape.Width);
    }

    [Fact]
    public void A_running_pomodoro_keeps_its_own_width_and_the_idle_width_never_shrinks_it()
    {
        var settings = new IslandSettings { CompactWidth = 124 };

        ShapeSize pomodoro = IslandShapeTable.For(IslandMode.Compact, settings, true, false, [], idleShown: false);
        ShapeSize wider = IslandShapeTable.For(IslandMode.Compact, settings with { CompactWidth = 200 }, false, false, [], idleShown: true);

        Assert.Equal(IslandShapeTable.PomodoroCompactWidth, pomodoro.Width);
        Assert.Equal(200, wider.Width);
    }

    [Fact]
    public void A_docked_capsule_keeps_its_length_because_its_idle_content_fits_the_existing_one()
    {
        var settings = new IslandSettings { CompactWidth = 124, CompactHeight = 36 };

        ShapeSize shape = IslandShapeTable.For(IslandMode.Compact, settings, false, false, [], vertical: true, idleShown: true);

        Assert.Equal(36, shape.Width);
        Assert.Equal(124, shape.Height);
    }

    [Theory]
    [InlineData(IslandMode.Expanded)]
    [InlineData(IslandMode.Mini)]
    [InlineData(IslandMode.MediaPreview)]
    public void Other_modes_are_not_affected_by_the_idle_content(IslandMode mode)
    {
        var settings = new IslandSettings();

        ShapeSize without = IslandShapeTable.For(mode, settings, false, false, ["nowplaying"], idleShown: false);
        ShapeSize with = IslandShapeTable.For(mode, settings, false, false, ["nowplaying"], idleShown: true);

        Assert.Equal(without, with);
    }
}
