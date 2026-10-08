using Island.Core.Pomodoro;

namespace Island.Core.Tests;

public class BlockedSitesTests
{
    [Fact]
    public void Default_lists_the_four_blocked_sites_in_order()
    {
        Assert.Equal(new[] { "YouTube", "Twitter/X", "Instagram", "Reddit" }, BlockedSites.Default.Select(s => s.Name));
    }

    [Theory]
    [InlineData("Lo-fi beats - YouTube - Google Chrome", "YouTube")]
    [InlineData("Home / X - Microsoft Edge", "Twitter/X")]
    [InlineData("(3) Home / X", "Twitter/X")]
    [InlineData("Elon Musk on X: \"hi\" - Mozilla Firefox", "Twitter/X")]
    [InlineData("Twitter - Brave", "Twitter/X")]
    [InlineData("Instagram", "Instagram")]
    [InlineData("Reels - Instagram - Opera", "Instagram")]
    [InlineData("r/programming - Reddit", "Reddit")]
    [InlineData("Reddit - Dive into anything", "Reddit")]
    [InlineData("Posts in (r/dotnet) - Vivaldi", "Reddit")]
    [InlineData("(3) Página Inicial / X and 10 more pages - Personal - Microsoft​ Edge", "Twitter/X")]
    [InlineData("Home / X and 2 more pages - Microsoft Edge", "Twitter/X")]
    [InlineData("Notifications / X e mais 4 páginas - Pessoal - Microsoft Edge", "Twitter/X")]
    [InlineData("Explore / X - Google Chrome", "Twitter/X")]
    public void Titles_of_blocked_sites_match_their_site(string title, string site)
    {
        Assert.Equal(site, BlockedSites.Match(title));
    }

    [Theory]
    [InlineData("Visual Studio Code")]
    [InlineData("Gmail - Inbox")]
    [InlineData("Next.js docs")]
    [InlineData("Dynamic Island - File Explorer")]
    [InlineData("Max / Xavier notes")]
    public void Unrelated_titles_do_not_match(string title)
    {
        Assert.Null(BlockedSites.Match(title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_titles_do_not_match(string? title)
    {
        Assert.Null(BlockedSites.Match(title));
    }

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("MSEDGE.EXE", true)]
    [InlineData("firefox", true)]
    [InlineData("opera_gx", true)]
    [InlineData("notepad", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Browser_processes_are_recognised_ignoring_case_and_exe_suffix(string? processName, bool expected)
    {
        Assert.Equal(expected, BlockedSites.IsBrowserProcess(processName));
    }
}
