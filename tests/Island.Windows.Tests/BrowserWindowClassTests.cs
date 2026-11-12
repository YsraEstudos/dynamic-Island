using Island.Core.Pomodoro;
using Island.Windows.Configuration;
using Island.Windows.Focus;

namespace Island.Windows.Tests;

/// <summary>
/// PC-04/PC-05: a portable or renamed browser escapes a process-name allowlist, but its native window class still
/// identifies a Chromium/Gecko window. These tests guard the class fallback used by the island and by the guardian.
/// </summary>
public sealed class BrowserWindowClassTests
{
    [Theory]
    [InlineData("Chrome_WidgetWin_1", true)]
    [InlineData("chrome_widgetwin_1", true)] // the class comparison ignores case
    [InlineData("MozillaWindowClass", true)]
    [InlineData("IEFrame", true)]
    [InlineData("Notepad", false)]
    [InlineData("HwndWrapper[DefaultDomain;;guid]", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Browser_window_classes_are_recognized(string? windowClass, bool expected)
    {
        Assert.Equal(expected, BlockedSites.IsBrowserWindowClass(windowClass));
    }

    [Fact]
    public void A_renamed_portable_browser_is_caught_by_its_window_class()
    {
        // "ff.exe" / "meubrowser.exe" are not in the process allowlist; the native class still identifies the browser.
        Assert.False(BlockedSites.IsBrowserProcess("meubrowser"));
        Assert.True(BlockedSites.IsBrowserWindow("meubrowser", "MozillaWindowClass"));

        Assert.True(ForegroundSiteGuard.ShouldBlockWithWindowClass("meubrowser", "MozillaWindowClass",
            "XVideos - Perfil", SiteGuardMode.All, out string? siteName));
        Assert.Equal("Conteúdo adulto", siteName);
    }

    [Fact]
    public void A_class_match_alone_never_blocks_a_harmless_title()
    {
        Assert.False(ForegroundSiteGuard.ShouldBlockWithWindowClass("code", "Chrome_WidgetWin_1",
            "Program.cs - Visual Studio Code", SiteGuardMode.All, out _));
        Assert.False(ForegroundSiteGuard.ShouldBlockWithWindowClass("meubrowser", "MozillaWindowClass",
            "Notas da reunião", SiteGuardMode.All, out _));
    }

    [Fact]
    public void Electron_and_webview_windows_are_checked_with_the_same_rules()
    {
        Assert.True(ForegroundSiteGuard.ShouldBlockWithWindowClass("msedgewebview2", "Chrome_WidgetWin_1",
            "reddit.com/r/brasil - WebView", SiteGuardMode.All, out string? siteName));
        Assert.Equal("Reddit", siteName);

        Assert.True(ForegroundSiteGuard.ShouldBlockWithWindowClass("steamwebhelper", "Chrome_WidgetWin_1",
            "YouTube - Steam Overlay", SiteGuardMode.All, out siteName));
        Assert.Equal("YouTube", siteName);
    }

    [Fact]
    public void The_break_release_mode_applies_to_class_matched_windows_too()
    {
        Assert.False(ForegroundSiteGuard.ShouldBlockWithWindowClass("meubrowser", "MozillaWindowClass",
            "Instagram", SiteGuardMode.BreakRelease, out _));
        Assert.True(ForegroundSiteGuard.ShouldBlockWithWindowClass("meubrowser", "MozillaWindowClass",
            "Instagram", SiteGuardMode.All, out _));
        Assert.True(ForegroundSiteGuard.ShouldBlockWithWindowClass("meubrowser", "MozillaWindowClass",
            "XVideos", SiteGuardMode.BreakRelease, out _));
    }

    [Fact]
    public void The_class_fallback_is_off_when_the_guard_is_off()
    {
        Assert.False(ForegroundSiteGuard.ShouldBlockWithWindowClass("meubrowser", "MozillaWindowClass",
            "YouTube", SiteGuardMode.Off, out _));
    }
}

public sealed class SettingsTamperWatchTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"settings-watch-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { }
    }

    [Fact]
    public async Task An_external_edit_is_reported()
    {
        File.WriteAllText(_path, "{\"phoneBlockEnabled\":true}");
        using var watch = new SettingsTamperWatch(_path, TimeSpan.FromMilliseconds(40));
        var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        watch.ChangedExternally += () => changed.TrySetResult(true);
        watch.Start();

        File.WriteAllText(_path, "{\"phoneBlockEnabled\":false}");

        Assert.True(await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(5))) == changed.Task);
    }

    [Fact]
    public async Task The_apps_own_save_is_not_reported_as_tampering()
    {
        File.WriteAllText(_path, "{}");
        using var watch = new SettingsTamperWatch(_path, TimeSpan.FromMilliseconds(40));
        int changes = 0;
        watch.ChangedExternally += () => Interlocked.Increment(ref changes);
        watch.Start();

        File.WriteAllText(_path, "{\"phoneBlockEnabled\":true}");
        watch.NoteApplicationSave();
        await Task.Delay(300);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Starting_after_the_file_disappeared_reports_the_removal()
    {
        using var watch = new SettingsTamperWatch(_path, TimeSpan.FromMilliseconds(40));
        int changes = 0;
        watch.ChangedExternally += () => Interlocked.Increment(ref changes);
        File.WriteAllText(_path, "{}");
        watch.Start();

        File.Delete(_path);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref changes) == 0 && DateTime.UtcNow < deadline) Thread.Sleep(20);

        Assert.True(Volatile.Read(ref changes) > 0);
    }
}
