using Island.Windows.Focus;

namespace Island.Windows.Tests;

public sealed class ForegroundSiteGuardTests
{
    [Theory]
    [InlineData("chrome", "Lo-fi - YouTube - Google Chrome", "YouTube")]
    [InlineData("msedge", "Home / X - Microsoft Edge", "Twitter/X")]
    [InlineData("firefox", "r/dotnet - Reddit", "Reddit")]
    public void ShouldBlock_BrowserOnBlockedSite_ReturnsSiteName(string processName, string title, string expectedSite)
    {
        bool blocked = ForegroundSiteGuard.ShouldBlock(processName, title, out string? siteName);

        Assert.True(blocked);
        Assert.Equal(expectedSite, siteName);
    }

    [Theory]
    [InlineData("notepad", "YouTube notes.txt - Notepad")]   // a blocked name in a non-browser window
    [InlineData("chrome", "Gmail - Inbox")]                   // a browser on an allowed site
    [InlineData(null, "Lo-fi - YouTube - Google Chrome")]     // process lookup failed: never block
    [InlineData("chrome", null)]                              // no title to match
    public void ShouldBlock_NotBlockedCase_ReturnsFalseWithoutSiteName(string? processName, string? title)
    {
        bool blocked = ForegroundSiteGuard.ShouldBlock(processName, title, out string? siteName);

        Assert.False(blocked);
        Assert.Null(siteName);
    }

    [Theory]
    [InlineData("msedge", "FIscal", "(3) Página Inicial / X - Microsoft Edge", "Twitter/X")]
    [InlineData("chrome", "Work", "Lo-fi - YouTube - Google Chrome", "YouTube")]
    [InlineData("msedge", "FIscal", "Gmail - Microsoft Edge", null)]
    [InlineData("notepad", "FIscal", "Home / X", null)]
    [InlineData("Codex", "FIscal", "Home / X", null)]
    [InlineData("msedge", "FIscal", null, null)]
    [InlineData("msedge", "Home / X - Microsoft Edge", null, "Twitter/X")]
    public void ShouldBlock_NamedWindow_UsesActiveBrowserTitle(string processName, string windowTitle,
        string? browserTitle, string? expectedSite)
    {
        bool blocked = ForegroundSiteGuard.ShouldBlock(processName, windowTitle, browserTitle, out string? siteName);

        Assert.Equal(expectedSite is not null, blocked);
        Assert.Equal(expectedSite, siteName);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(-4, 0, true)]
    [InlineData(-4, -1205, true)] // Chromium uses negative child IDs for accessibility nodes.
    [InlineData(0, 12, false)]
    [InlineData(-3, 0, false)]
    public void IsBrowserTitleChange_IncludesAccessibleClientEvents(int objectId, int childId, bool expected)
    {
        Assert.Equal(expected, ForegroundSiteGuard.IsBrowserTitleChange(objectId, childId));
    }

    [Fact]
    public void SetActiveFalseThenDispose_CanBeRepeatedWithoutThrowing()
    {
        var guard = new ForegroundSiteGuard();

        guard.SetActive(false);
        guard.Dispose();
        guard.Dispose();   // idempotent

        Assert.False(guard.Active);
    }
}
