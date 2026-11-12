namespace SysGuard.Tests;

using SysGuard;

public sealed class AdultContentTests
{
    [Theory]
    [InlineData("Pornhub - Google Chrome")]
    [InlineData("XVideos - Microsoft Edge")]
    [InlineData("Free Porn Videos - XVIDEOS.COM")]
    [InlineData("Chaturbate - Free Adult Webcams")]
    [InlineData("OnlyFans - Mozilla Firefox")]
    [InlineData("Pornô brasileiro - Opera")]
    [InlineData("Vídeos de sexo amador - Opera")]
    [InlineData("Novinhas brasileiras - Google Chrome")]
    [InlineData("Brazilian porn - Google Chrome")]
    public void Adult_titles_are_detected(string title)
    {
        Assert.True(AdultContent.IsAdultTitle(title), title);
    }

    [Theory]
    [InlineData("Notepad")]
    [InlineData("YouTube - Google Chrome")]
    [InlineData("Instagram")]
    [InlineData("BBC News")]
    [InlineData("Sex Education - Netflix")]
    [InlineData("Essex - Wikipedia")]
    [InlineData("Programação em C# - Microsoft Learn")]
    [InlineData("")]
    public void Ordinary_titles_are_not_blocked(string? title)
    {
        Assert.False(AdultContent.IsAdultTitle(title), title);
    }

    [Fact]
    public void Null_title_is_not_blocked()
    {
        Assert.False(AdultContent.IsAdultTitle(null));
    }

    [Fact]
    public void Pathological_long_title_does_not_throw()
    {
        Assert.False(AdultContent.IsAdultTitle(new string('a', 200_000)));
    }

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("Chrome.exe", true)]
    [InlineData("msedge", true)]
    [InlineData("firefox", true)]
    [InlineData("opera_gx", true)]
    [InlineData("notepad", false)]
    [InlineData("explorer", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Browser_processes_are_recognized(string? processName, bool expected)
    {
        Assert.Equal(expected, AdultContent.IsBrowserProcess(processName));
    }
}

public sealed class DnsControllerTests
{
    [Fact]
    public void Same_servers_compares_in_order()
    {
        Assert.True(DnsController.SameServers(
            [DnsController.AdultV4Primary, DnsController.AdultV4Secondary],
            DnsController.AdultV4));
        Assert.False(DnsController.SameServers(
            [DnsController.AdultV4Secondary, DnsController.AdultV4Primary],
            DnsController.AdultV4));
    }

    [Fact]
    public void Same_servers_normalizes_ipv4_mapped_addresses()
    {
        Assert.True(DnsController.SameServers(["::ffff:1.1.1.1"], ["1.1.1.1"]));
    }

    [Fact]
    public void Servers_are_the_cleanbrowsing_adult_resolvers()
    {
        // Adult: bloqueia pornografia no DNS sem forçar o modo restrito do YouTube (o Family forçava).
        Assert.Equal("185.228.168.10", DnsController.AdultV4Primary);
        Assert.Equal("185.228.169.11", DnsController.AdultV4Secondary);
        Assert.Equal("2a0d:2a00:1::1", DnsController.AdultV6Primary);
        Assert.Equal("2a0d:2a00:2::1", DnsController.AdultV6Secondary);
    }

    [Fact]
    public void Old_family_resolvers_are_not_used_anymore()
    {
        Assert.DoesNotContain("185.228.168.168", DnsController.AdultV4);
        Assert.DoesNotContain("185.228.169.168", DnsController.AdultV4);
        Assert.DoesNotContain("2a0d:2a00:1::", DnsController.AdultV6);
        Assert.DoesNotContain("2a0d:2a00:2::", DnsController.AdultV6);
    }

    [Fact]
    public void Dns_original_is_static_only_when_nameserver_exists()
    {
        var dhcp = DnsController.DecideOriginal(null, "ipv4", ["1.1.1.1"]);
        Assert.False(dhcp.HadStatic);
        Assert.Empty(dhcp.Servers);

        var empty = DnsController.DecideOriginal("   ", "ipv4", ["1.1.1.1"]);
        Assert.False(empty.HadStatic);

        var stat = DnsController.DecideOriginal("8.8.8.8, 8.8.4.4", "ipv4", ["1.1.1.1"]);
        Assert.True(stat.HadStatic);
        Assert.Equal(["8.8.8.8", "8.8.4.4"], stat.Servers);

        var v6 = DnsController.DecideOriginal("2001:4860:4860::8888", "ipv6", []);
        Assert.True(v6.HadStatic);
        Assert.Equal(["2001:4860:4860::8888"], v6.Servers);
    }

    [Fact]
    public void Is_our_resolver_recognizes_adult_and_legacy_family_addresses()
    {
        Assert.True(DnsController.IsOurResolver("185.228.168.10"));
        Assert.True(DnsController.IsOurResolver("185.228.168.168"));   // Family da 1.0
        Assert.True(DnsController.IsOurResolver("2a0d:2a00:1::1"));
        Assert.True(DnsController.IsOurResolver("2a0d:2a00:1::"));     // Family da 1.0
        Assert.True(DnsController.IsOurResolver("::ffff:185.228.168.10"));
        Assert.False(DnsController.IsOurResolver("1.1.1.1"));
        Assert.False(DnsController.IsOurResolver("8.8.8.8"));
    }

    [Fact]
    public void Normalize_ip_matches_system_canonical_form()
    {
        Assert.Equal("1.1.1.1", DnsController.NormalizeIp("::ffff:1.1.1.1"));
        Assert.Equal(
            System.Net.IPAddress.Parse("2A0D:2A00:1::").ToString(),
            DnsController.NormalizeIp("2A0D:2A00:1::"));
    }
}

public sealed class BrowserPoliciesTests
{
    [Fact]
    public void Desired_policies_cover_chrome_edge_and_firefox()
    {
        Assert.Contains(BrowserPolicies.Desired, p =>
            p.KeyPath == @"SOFTWARE\Policies\Google\Chrome" && p.ValueName == "DnsOverHttpsMode" && p.StringValue == "off");
        Assert.Contains(BrowserPolicies.Desired, p =>
            p.KeyPath == @"SOFTWARE\Policies\Microsoft\Edge" && p.ValueName == "ForceBingSafeSearch" && p.DwordValue == 2);
        Assert.Contains(BrowserPolicies.Desired, p =>
            p.KeyPath == @"SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS" && p.ValueName == "Enabled" && p.DwordValue == 0);
        Assert.Contains(BrowserPolicies.Desired, p =>
            p.KeyPath == @"SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS" && p.ValueName == "Locked" && p.DwordValue == 1);
    }

    [Fact]
    public void Desired_policies_keep_search_safesearch_but_not_youtube_restrict()
    {
        // SafeSearch de busca (Google/Bing) continua; o modo restrito de vídeo do YouTube foi removido.
        Assert.Contains(BrowserPolicies.Desired, p =>
            p.KeyPath == @"SOFTWARE\Policies\Google\Chrome" && p.ValueName == "ForceGoogleSafeSearch" && p.DwordValue == 1);
        Assert.Contains(BrowserPolicies.Desired, p =>
            p.KeyPath == @"SOFTWARE\Policies\Microsoft\Edge" && p.ValueName == "ForceGoogleSafeSearch" && p.DwordValue == 1);

        Assert.DoesNotContain(BrowserPolicies.Desired, p => p.ValueName == "ForceYouTubeRestrict");
        Assert.DoesNotContain(BrowserPolicies.Desired, p => p.KeyPath.Contains("YouTube", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void State_key_is_stable()
    {
        Assert.Equal(
            @"SOFTWARE\Policies\Google\Chrome|DnsOverHttpsMode",
            BrowserPolicies.StateKey(@"SOFTWARE\Policies\Google\Chrome", "DnsOverHttpsMode"));
    }
}

public sealed class ScServiceTests
{
    [Fact]
    public void Create_quotes_whole_binary_path_with_service_argument()
    {
        var runner = new RecordingCommandRunner();
        var result = ScService.Create(runner, @"C:\ProgramData\SysGuard\SysGuard.exe");

        Assert.True(result.Success);
        var call = Assert.Single(runner.Calls);
        Assert.Contains("create SysGuardSvc", call.Arguments);
        Assert.Contains("binPath= \"C:\\ProgramData\\SysGuard\\SysGuard.exe --service\"", call.Arguments);
        Assert.Contains("start= auto", call.Arguments);
        Assert.Contains("DisplayName= \"System Guard\"", call.Arguments);
    }

    [Fact]
    public void Create_falls_back_to_config_when_service_already_exists()
    {
        var runner = new RecordingCommandRunner { Result = new CommandResult(1073, "já existe") };
        ScService.Create(runner, @"C:\ProgramData\SysGuard\SysGuard.exe");

        Assert.Equal(2, runner.Calls.Count);
        Assert.StartsWith("config SysGuardSvc", runner.Calls[1].Arguments);
    }

    [Theory]
    [InlineData("STATE       : 4  RUNNING", 4)]
    [InlineData("STATE       : 1  STOPPED", 1)]
    [InlineData("STATE       : 3  STOP_PENDING", 3)]
    [InlineData("STATE       : 2  START_PENDING", 2)]
    [InlineData("lixo sem estado", -1)]
    public void Parses_service_state_code(string output, int expected)
    {
        Assert.Equal(expected, ScService.ParseStateCode(output));
    }

    [Fact]
    public void Recovery_restarts_after_5_10_and_30_seconds_and_resets_daily()
    {
        var runner = new RecordingCommandRunner();
        ScService.ConfigureRecovery(runner);
        var call = Assert.Single(runner.Calls);
        Assert.Contains("failure SysGuardSvc", call.Arguments);
        Assert.Contains("reset= 86400", call.Arguments);
        Assert.Contains("restart/5000/restart/10000/restart/30000", call.Arguments);
    }

    [Fact]
    public void Recovery_can_be_configured_for_the_watchdog_service()
    {
        var runner = new RecordingCommandRunner();
        ScService.ConfigureRecovery(runner, AppInfo.WatchdogServiceName);
        var call = Assert.Single(runner.Calls);
        Assert.Contains("failure " + AppInfo.WatchdogServiceName, call.Arguments);
        Assert.Contains("reset= 86400", call.Arguments);
    }

    [Fact]
    public void Watchdog_service_is_created_with_its_own_argument()
    {
        var runner = new RecordingCommandRunner();
        ScService.CreateWatchdog(runner, @"C:\ProgramData\SysGuard\SysGuard.exe");

        var call = Assert.Single(runner.Calls);
        Assert.Contains("create " + AppInfo.WatchdogServiceName, call.Arguments);
        Assert.Contains("binPath= \"C:\\ProgramData\\SysGuard\\SysGuard.exe --watchdog-service\"", call.Arguments);
        Assert.Contains("DisplayName= \"System Guard Watchdog\"", call.Arguments);
        Assert.Contains("start= auto", call.Arguments);
    }

    [Fact]
    public void Named_service_operations_use_the_given_service_name()
    {
        var runner = new RecordingCommandRunner();
        ScService.Stop(runner, AppInfo.WatchdogServiceName);
        ScService.Delete(runner, AppInfo.WatchdogServiceName);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("stop " + AppInfo.WatchdogServiceName, runner.Calls[0].Arguments);
        Assert.Equal("delete " + AppInfo.WatchdogServiceName, runner.Calls[1].Arguments);
    }

    [Fact]
    public void Watchdog_and_main_service_names_are_distinct()
    {
        Assert.NotEqual(AppInfo.ServiceName, AppInfo.WatchdogServiceName);
        Assert.EndsWith(AppInfo.WatchdogServiceName, AppInfo.ServiceRegistryPathFor(AppInfo.WatchdogServiceName));
    }
}
