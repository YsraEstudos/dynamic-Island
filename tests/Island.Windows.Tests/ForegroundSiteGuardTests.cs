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
