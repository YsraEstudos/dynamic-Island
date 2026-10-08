using System.Text.Json;
using Island.Core.Models;
using Island.Windows.Updates;

namespace Island.Windows.Tests;

public sealed class UpdateRulesTests
{
    private const string Package = "https://github.com/YsraEstudos/dynamic-Island/releases/download/v0.2.0/DynamicIsland-0.2.0-win-x64.zip";

    private static string Release(string tag, params (string Name, string Url)[] assets) =>
        JsonSerializer.Serialize(new
        {
            tag_name = tag,
            assets = assets.Select(a => new { name = a.Name, browser_download_url = a.Url }).ToArray(),
        });

    [Theory]
    [InlineData("v0.2.0", "0.2.0.0")]
    [InlineData("V1.4", "1.4.0.0")]
    [InlineData("0.2.0.7", "0.2.0.7")]
    [InlineData(" v0.2.0 ", "0.2.0.0")]
    public void Tags_parse_to_four_part_versions(string tag, string expected)
    {
        Assert.True(UpdateRules.TryParseTag(tag, out Version version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("latest")]
    [InlineData("v0.2.0-beta")]
    [InlineData("v1.2.3.4.5")]
    public void Garbage_and_prerelease_tags_do_not_parse(string? tag) =>
        Assert.False(UpdateRules.TryParseTag(tag, out _));

    [Fact]
    public void A_newer_release_with_a_zip_is_an_update()
    {
        UpdateInfo? update = UpdateRules.Evaluate(Release("v0.2.0", ("DynamicIsland-0.2.0-win-x64.zip", Package)), new Version(0, 1, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 2, 0, 0), update.Version);
        Assert.Equal("v0.2.0", update.Tag);
        Assert.Equal(Package, update.DownloadUrl.ToString());
    }

    [Theory]
    [InlineData("v0.1.0")] // equal
    [InlineData("v0.1")] // equal once padded to four parts
    [InlineData("v0.0.9")] // older
    public void Equal_or_older_releases_are_not_updates(string tag) =>
        Assert.Null(UpdateRules.Evaluate(Release(tag, ("a.zip", Package)), new Version(0, 1, 0)));

    [Fact]
    public void A_fourth_part_bump_is_an_update()
    {
        Assert.NotNull(UpdateRules.Evaluate(Release("v0.1.0.1", ("a.zip", Package)), new Version(0, 1, 0)));
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("v0.2.0-beta")]
    public void Unparseable_tags_are_never_updates(string tag) =>
        Assert.Null(UpdateRules.Evaluate(Release(tag, ("a.zip", Package)), new Version(0, 1, 0)));

    [Fact]
    public void The_first_zip_asset_is_chosen_and_the_others_are_ignored()
    {
        const string first = "https://github.com/YsraEstudos/dynamic-Island/releases/download/v0.2.0/first.zip";
        const string second = "https://github.com/YsraEstudos/dynamic-Island/releases/download/v0.2.0/second.zip";

        UpdateInfo? update = UpdateRules.Evaluate(
            Release("v0.2.0", ("notes.txt", "https://github.com/YsraEstudos/dynamic-Island/notes.txt"), ("first.zip", first), ("second.zip", second)),
            new Version(0, 1, 0));

        Assert.NotNull(update);
        Assert.Equal(first, update.DownloadUrl.ToString());
    }

    [Fact]
    public void A_release_without_a_zip_is_not_an_update()
    {
        Assert.Null(UpdateRules.Evaluate(Release("v0.2.0", ("setup.exe", Package), ("notes.txt", Package)), new Version(0, 1, 0)));
    }

    [Theory]
    [InlineData("http://github.com/YsraEstudos/dynamic-Island/releases/download/v0.2.0/a.zip")] // not HTTPS
    [InlineData("https://evil.example.com/a.zip")] // other host
    [InlineData("https://github.com.evil.example/a.zip")] // look-alike host
    [InlineData("https://github.com@evil.example/a.zip")] // user-info trick: the host is evil.example
    [InlineData("https://github.com:8443/YsraEstudos/dynamic-Island/a.zip")] // not the default port
    [InlineData("file:///C:/Windows/System32/a.zip")]
    [InlineData("not a url")]
    public void Only_https_github_addresses_are_trusted(string url) =>
        Assert.Null(UpdateRules.Evaluate(Release("v0.2.0", ("a.zip", url)), new Version(0, 1, 0)));

    [Theory]
    [InlineData("https://github.com/YsraEstudos/dynamic-Island/releases/download/v0.2.0/a.zip")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1/a.zip")]
    public void GitHub_https_addresses_are_trusted(string url) =>
        Assert.NotNull(UpdateRules.Evaluate(Release("v0.2.0", ("a.zip", url)), new Version(0, 1, 0)));

    [Theory]
    [InlineData("YsraEstudos/dynamic-Island")]
    [InlineData("owner/repo.name_1")]
    public void Owner_and_name_are_accepted(string repository) =>
        Assert.True(UpdateRules.IsValidRepository(repository));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("owner")]
    [InlineData("a/b/c")]
    [InlineData("../etc")]
    [InlineData("owner/..")]
    [InlineData("owner/repo?x=1")]
    [InlineData("owner/repo#frag")]
    [InlineData("owner name/repo")]
    public void Anything_else_is_refused_as_a_repository(string? repository) =>
        Assert.False(UpdateRules.IsValidRepository(repository));

    [Fact]
    public void An_msix_must_start_with_the_zip_signature()
    {
        string dir = Path.Combine(Path.GetTempPath(), "island-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string zip = Path.Combine(dir, "good.zip");
            File.WriteAllBytes(zip, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00]);
            string html = Path.Combine(dir, "login.zip");
            File.WriteAllText(html, "<html>sign in</html>");
            string tiny = Path.Combine(dir, "tiny.zip");
            File.WriteAllBytes(tiny, [0x50]);

            Assert.True(UpdateRules.LooksLikeZip(zip));
            Assert.False(UpdateRules.LooksLikeZip(html));
            Assert.False(UpdateRules.LooksLikeZip(tiny));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_update_script_waits_copies_then_reopens_the_app()
    {
        string script = UpdateRules.BuildUpdateScript(4242, "C:/u/it's", "C:/app", "C:/app/DynamicIsland.exe", "C:/logs/update.log");

        int wait = script.IndexOf("Wait-Process -Id 4242", StringComparison.Ordinal);
        int copy = script.IndexOf("robocopy 'C:/u/it''s' 'C:/app'", StringComparison.Ordinal);
        int start = script.IndexOf("Start-Process -FilePath 'C:/app/DynamicIsland.exe'", StringComparison.Ordinal);
        Assert.True(wait >= 0, "must wait for the old process");
        Assert.True(copy > wait, "files are copied only after the old app exited, and paths are quoted");
        Assert.True(start > copy, "the app is reopened only after the copy");
        Assert.Contains("-ge 8", script);
    }
}
