using Island.Windows.Display;

namespace Island.Windows.Tests;

public sealed class FullscreenDetectorTests
{
    [Fact]
    public void Evaluate_WithoutStart_MatchesIsFullscreenAppActive()
    {
        using var detector = new FullscreenDetector();

        bool result = detector.Evaluate();

        Assert.Equal(result, detector.IsFullscreenAppActive);
    }

    [Fact]
    public void Evaluate_RepeatedWithSameState_RaisesNoFurtherEvents()
    {
        using var detector = new FullscreenDetector();
        int raised = 0;
        detector.FullscreenChanged += (_, _) => raised++;

        detector.Evaluate();
        int afterFirst = raised;
        detector.Evaluate();   // foreground state unchanged: no new event

        Assert.Equal(afterFirst, raised);
    }

    [Fact]
    public void Start_And_Dispose_DoNotThrow()
    {
        var detector = new FullscreenDetector();

        detector.Start();
        detector.Start();   // idempotent
        detector.Dispose();
        detector.Dispose(); // idempotent
    }

    [Fact]
    public void MatchesGameList_ExactName_Matches()
    {
        Assert.True(FullscreenDetector.MatchesGameList("eldenring", ["eldenring"]));
    }

    [Fact]
    public void MatchesGameList_IgnoresCaseAndExeSuffix()
    {
        Assert.True(FullscreenDetector.MatchesGameList("EldenRing", ["eldenring.exe"]));
        Assert.True(FullscreenDetector.MatchesGameList("EldenRing.exe", ["ELDENRING"]));
    }

    [Fact]
    public void MatchesGameList_IgnoresSurroundingSpaces()
    {
        Assert.True(FullscreenDetector.MatchesGameList("  eldenring ", [" EldenRing.exe "]));
    }

    [Fact]
    public void MatchesGameList_EmptyList_NeverMatches()
    {
        Assert.False(FullscreenDetector.MatchesGameList("eldenring", []));
    }

    [Fact]
    public void MatchesGameList_DifferentOrPartialName_DoesNotMatch()
    {
        Assert.False(FullscreenDetector.MatchesGameList("elden", ["eldenring"]));
        Assert.False(FullscreenDetector.MatchesGameList("eldenring2", ["eldenring"]));
        Assert.False(FullscreenDetector.MatchesGameList("chrome", ["eldenring", "minecraft"]));
    }

    [Fact]
    public void MatchesGameList_BlankProcessName_DoesNotMatch()
    {
        Assert.False(FullscreenDetector.MatchesGameList("", [""]));
        Assert.False(FullscreenDetector.MatchesGameList("   ", ["eldenring"]));
    }
}
