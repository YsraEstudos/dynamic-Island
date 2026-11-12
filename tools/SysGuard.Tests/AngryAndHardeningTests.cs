namespace SysGuard.Tests;

using SysGuard;

public sealed class AngryLiveStateParserTests
{
    // Formato publicado pelo Dynamic Island (contrato combinado com o pc-hardening).
    private const string PublishedJson = """
        {
          "version": 1,
          "savedAtUtc": "2026-11-11T17:14:32.1234567+00:00",
          "savedAtMs": 1762881272123,
          "active": true,
          "locked": true,
          "scheduled": false,
          "phoneBlockEnabled": true,
          "phrase": "Eu desisto do meu foco agora, e sei disso.",
          "endUtc": "2026-11-11T17:38:12.1234567+00:00",
          "endMs": 1762882692123,
          "timer": {
            "phase": "Focus",
            "phaseDurationMs": 1500000,
            "remainingMs": 1420000,
            "running": true,
            "planTotal": 4,
            "planCycle": 2,
            "planFocusMs": 1500000,
            "planBreakMs": 300000,
            "autoAdvance": true,
            "sessionGoal": "Revisar matemática",
            "awaitingNextPhase": false
          }
        }
        """;

    private static readonly DateTimeOffset DuringFocus = DateTimeOffset.FromUnixTimeMilliseconds(1762882000000);
    private static readonly DateTimeOffset AfterEnd = DateTimeOffset.FromUnixTimeMilliseconds(1762882692124);

    [Fact]
    public void Published_file_blocks_during_locked_focus()
    {
        var state = AngryLiveStateParser.Parse(PublishedJson);
        Assert.True(state.Active);
        Assert.True(state.Locked);
        Assert.Equal(1762882692123L, state.EndUnixMs.GetValueOrDefault());
        Assert.Equal(1762881272123L, state.SavedAtUnixMs.GetValueOrDefault());
        Assert.Equal("Focus", state.TimerPhase);
        Assert.Equal(1420000L, state.TimerRemainingMs.GetValueOrDefault());
        Assert.True(state.TimerRunning);
        Assert.True(state.IsBlocking(DuringFocus));
        Assert.False(state.IsBreakRelease(DuringFocus));
        Assert.False(state.IsBlocking(AfterEnd));
        Assert.NotNull(state.Age(DuringFocus));
    }

    [Fact]
    public void Break_keeps_blocking_without_live_timer_data()
    {
        // Intervalo aguardando o Start (sem timer.phase/running): nada é liberado.
        var state = AngryLiveStateParser.Parse(
            """{"active":true,"locked":false,"endMs":1762882692123}""");
        Assert.True(state.IsGuarding(DuringFocus));
        Assert.False(state.IsBreakRelease(DuringFocus));
        Assert.True(state.IsBlocking(DuringFocus));
    }

    [Fact]
    public void Parsed_break_fields_feed_the_release_window()
    {
        var state = AngryLiveStateParser.Parse("""
            {"savedAtMs":1762882000000,"active":true,"locked":false,"endMs":1762882692123,
             "timer":{"phase":"Break","remainingMs":60000,"running":true}}
            """);
        Assert.Equal("Break", state.TimerPhase);
        Assert.Equal(60000L, state.TimerRemainingMs.GetValueOrDefault());
        Assert.True(state.TimerRunning);
        Assert.True(state.IsGuarding(DuringFocus));
        Assert.True(state.IsBreakRelease(DuringFocus));
        Assert.False(state.IsBlocking(DuringFocus));
    }

    [Fact]
    public void Inactive_session_does_not_block()
    {
        var state = AngryLiveStateParser.Parse(
            """{"active":false,"locked":true,"endMs":1762882692123}""");
        Assert.False(state.IsBlocking(DuringFocus));
    }

    [Fact]
    public void Missing_or_malformed_json_never_blocks()
    {
        Assert.False(AngryLiveStateParser.Parse(null).IsBlocking(DuringFocus));
        Assert.False(AngryLiveStateParser.Parse("").IsBlocking(DuringFocus));
        Assert.False(AngryLiveStateParser.Parse("   ").IsBlocking(DuringFocus));
        Assert.False(AngryLiveStateParser.Parse("lixo").IsBlocking(DuringFocus));
        Assert.False(AngryLiveStateParser.Parse("[]").IsBlocking(DuringFocus));
        Assert.False(AngryLiveStateParser.Parse("{\"active\":true}").IsBlocking(DuringFocus));
        Assert.False(AngryLiveStateParser.Parse(new string('a', AngryLiveStateParser.MaxFileBytes + 1))
            .IsBlocking(DuringFocus));
    }

    [Fact]
    public void Field_names_are_case_insensitive()
    {
        var state = AngryLiveStateParser.Parse(
            """{"Active":true,"Locked":true,"EndMs":1762882692123,"SavedAtMs":1762881272123}""");
        Assert.True(state.IsBlocking(DuringFocus));
        Assert.Equal(1762882692123L, state.EndUnixMs.GetValueOrDefault());
    }

    [Fact]
    public void Boolean_and_numeric_variants_are_tolerated()
    {
        Assert.True(AngryLiveStateParser.Parse(
            """{"active":"true","locked":1,"endMs":"1762882692123"}""").IsBlocking(DuringFocus));
        Assert.True(AngryLiveStateParser.Parse(
            """{"isActive":true,"isLocked":true,"endsAtMs":1762882692123}""").IsBlocking(DuringFocus));
        Assert.False(AngryLiveStateParser.Parse(
            """{"active":"false","locked":true,"endMs":1762882692123}""").IsBlocking(DuringFocus));
    }

    [Fact]
    public void End_iso_utc_is_used_when_end_ms_is_absent()
    {
        var state = AngryLiveStateParser.Parse(
            """{"active":true,"locked":true,"endUtc":"2026-11-11T17:38:12.1234567+00:00"}""");
        var expected = DateTimeOffset.Parse(
            "2026-11-11T17:38:12.1234567+00:00", System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        Assert.Equal(expected, state.EndUnixMs.GetValueOrDefault());
        Assert.True(state.IsBlocking(DuringFocus));
    }

    [Fact]
    public void End_and_saved_at_inside_timer_are_accepted()
    {
        var state = AngryLiveStateParser.Parse(
            """{"active":true,"locked":true,"timer":{"endMs":1762882692123,"savedAtMs":1762881272123}}""");
        Assert.True(state.IsBlocking(DuringFocus));
        Assert.Equal(1762881272123L, state.SavedAtUnixMs.GetValueOrDefault());
    }

    [Fact]
    public void Saved_at_plus_remaining_is_a_fallback()
    {
        var state = AngryLiveStateParser.Parse(
            """{"active":true,"locked":true,"savedAtMs":1762881272123,"timer":{"remainingMs":1420000}}""");
        Assert.Equal(1762882692123L, state.EndUnixMs.GetValueOrDefault());
        Assert.True(state.IsBlocking(DuringFocus));
    }

    [Fact]
    public void Epoch_seconds_are_normalized_to_milliseconds()
    {
        var state = AngryLiveStateParser.Parse(
            """{"active":true,"locked":true,"endMs":1762882692}""");
        Assert.Equal(1762882692000L, state.EndUnixMs.GetValueOrDefault());
        Assert.True(state.IsBlocking(DuringFocus));
    }

    [Fact]
    public void Pathological_nesting_does_not_throw()
    {
        var deep = new string('[', 200) + new string(']', 200);
        Assert.False(AngryLiveStateParser.Parse(deep).IsBlocking(DuringFocus));
    }

    [Fact]
    public void Additive_fields_from_newer_versions_are_ignored()
    {
        // O contrato com o Dynamic Island é aditivo: campos novos (ex.: phoneBlockEnabled) não podem
        // quebrar a leitura do bloqueio durante uma sessão travada.
        var state = AngryLiveStateParser.Parse("""
            {
              "version": 2,
              "savedAtUtc": "2026-11-11T17:14:32.1234567+00:00",
              "savedAtMs": 1762881272123,
              "active": true,
              "locked": true,
              "phoneBlockEnabled": true,
              "futureField": { "nested": [1, 2, 3] },
              "endMs": 1762882692123,
              "timer": { "phase": "Focus", "remainingMs": 1420000, "running": true, "futureTimerField": "x" }
            }
            """);
        Assert.True(state.IsBlocking(DuringFocus));
    }
}

public sealed class AngryBreakReleaseTests
{
    private const long SavedAtMs = 1762882000000L;
    private const long EndMs = 1762882692123L;
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(SavedAtMs);

    private static AngryLiveState Break(long remainingMs, bool running = true, bool locked = false,
        string? phase = "Break", long? savedAtMs = SavedAtMs) =>
        new(true, locked, EndMs, savedAtMs, phase, remainingMs, running);

    [Fact]
    public void Break_one_second_before_the_window_still_blocks()
    {
        var state = Break(61_000);
        Assert.True(state.IsGuarding(Now));
        Assert.False(state.IsBreakRelease(Now));
        Assert.True(state.IsBlocking(Now));
    }

    [Theory]
    [InlineData(60_000)]
    [InlineData(1)]
    public void Release_window_starts_at_sixty_seconds(long remainingMs)
    {
        var state = Break(remainingMs);
        Assert.True(state.IsGuarding(Now));
        Assert.True(state.IsBreakRelease(Now));
        Assert.False(state.IsBlocking(Now));
    }

    [Fact]
    public void Release_window_closes_when_the_break_ends()
    {
        Assert.False(Break(60_000).IsBreakRelease(Now.AddSeconds(60))); // remainingNow == 0
        Assert.False(Break(1).IsBreakRelease(Now.AddMilliseconds(1)));
    }

    [Fact]
    public void Paused_break_never_releases()
    {
        // Intervalo esperando o Start manual: a janela não abre.
        var state = Break(30_000, running: false);
        Assert.True(state.IsGuarding(Now));
        Assert.False(state.IsBreakRelease(Now));
        Assert.True(state.IsBlocking(Now));
    }

    [Fact]
    public void Focus_phase_never_releases()
    {
        var state = Break(30_000, phase: "Focus");
        Assert.False(state.IsBreakRelease(Now));
        Assert.True(state.IsBlocking(Now));
    }

    [Fact]
    public void Locked_break_never_releases()
    {
        var state = Break(30_000, locked: true);
        Assert.False(state.IsBreakRelease(Now));
        Assert.True(state.IsBlocking(Now));
    }

    [Fact]
    public void Missing_saved_at_never_releases()
    {
        var state = Break(30_000, savedAtMs: null);
        Assert.False(state.IsBreakRelease(Now));
        Assert.True(state.IsBlocking(Now));
    }

    [Fact]
    public void Stale_snapshot_closes_the_release_after_the_minute()
    {
        // A ilha morreu e ninguém reescreveu o arquivo: a liberação dura o que os 60 s calculados a
        // partir do savedAtMs permitirem e fecha sozinha depois (a sessão continua bloqueada).
        var state = Break(60_000);
        Assert.True(state.IsBreakRelease(Now));
        Assert.False(state.IsBreakRelease(Now.AddSeconds(61)));
        Assert.True(state.IsBlocking(Now.AddSeconds(61)));
    }

    [Fact]
    public void Session_end_stops_guarding_and_releasing()
    {
        var ended = Now.AddSeconds(10);
        var state = new AngryLiveState(
            true, false, ended.ToUnixTimeMilliseconds(), SavedAtMs, "Break", 60_000, true);
        Assert.False(state.IsGuarding(ended));
        Assert.False(state.IsBreakRelease(ended));
        Assert.False(state.IsBlocking(ended));
    }

    [Fact]
    public void Inactive_session_never_releases()
    {
        var state = new AngryLiveState(false, false, EndMs, SavedAtMs, "Break", 60_000, true);
        Assert.False(state.IsBreakRelease(Now));
        Assert.False(state.IsBlocking(Now));
    }

    [Fact]
    public void Default_constructor_values_keep_previous_semantics()
    {
        // Compatibilidade: `new AngryLiveState(active, locked, endMs)` continua bloqueando o foco e
        // nunca libera um intervalo sem os campos do timer.
        Assert.True(new AngryLiveState(true, true, EndMs).IsBlocking(Now));
        var breakWithoutTimer = new AngryLiveState(true, false, EndMs);
        Assert.True(breakWithoutTimer.IsBlocking(Now));
        Assert.False(breakWithoutTimer.IsBreakRelease(Now));
    }
}

public sealed class AngryLiveCacheTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeMilliseconds(1762882000000);

    [Fact]
    public void Reads_files_at_most_once_per_three_seconds()
    {
        int reads = 0;
        var cache = new AngryLiveCache(
            files: () => ["/tmp/angry-live.json"],
            reader: (_, _) =>
            {
                reads++;
                return AngryLiveState.Inactive;
            });

        cache.Read(T0);
        cache.Read(T0.AddMilliseconds(1000));
        cache.Read(T0.AddMilliseconds(2999));
        Assert.Equal(1, reads);

        cache.Read(T0.AddMilliseconds(3000));
        Assert.Equal(2, reads);
        Assert.Equal(2, cache.ReadCount);
    }

    [Fact]
    public void Refresh_picks_up_a_new_state_after_the_window()
    {
        int reads = 0;
        var cache = new AngryLiveCache(
            files: () => ["/tmp/angry-live.json"],
            reader: (_, _) =>
            {
                reads++;
                return reads == 1
                    ? AngryLiveState.Inactive
                    : new AngryLiveState(true, true, T0.AddMinutes(10).ToUnixTimeMilliseconds());
            });

        Assert.False(cache.Read(T0).IsBlocking(T0));
        Assert.True(cache.Read(T0.AddSeconds(3)).IsBlocking(T0.AddSeconds(3)));
    }

    [Fact]
    public void Reader_exception_is_swallowed_and_returns_inactive()
    {
        var cache = new AngryLiveCache(
            files: () => ["/tmp/angry-live.json"],
            reader: (_, _) => throw new IOException("boom"));
        Assert.False(cache.Read(T0).IsBlocking(T0));
    }
}

public sealed class AngryLivePathsTests
{
    [Fact]
    public void Current_user_path_points_into_the_dynamic_island_folder()
    {
        var path = AngryLivePaths.ForCurrentUser();
        Assert.EndsWith(Path.Combine(AngryLivePaths.AppFolderName, AngryLivePaths.FileName), path);
    }

    [Fact]
    public void Profile_path_uses_appdata_local()
    {
        var path = AngryLivePaths.ForUserProfile(@"C:\Users\alguem");
        Assert.Equal(@"C:\Users\alguem\AppData\Local\DynamicIsland\angry-live.json", path);
    }

    [Fact]
    public void Enumerated_files_are_unique_and_well_formed()
    {
        IReadOnlyList<string> files;
        try
        {
            files = AngryLivePaths.EnumerateUserFiles();
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException("EnumerateUserFiles lançou: " + ex.Message);
        }

        Assert.NotEmpty(files);
        Assert.Equal(files.Count, files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(files, file => Assert.EndsWith(
            Path.Combine(AngryLivePaths.AppFolderName, AngryLivePaths.FileName), file));
    }
}

public sealed class AngryLiveStateReaderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1762882000000);

    private static string WriteState(string json, out string directory)
    {
        directory = TestPaths.NewDirectory();
        var file = Path.Combine(directory, "angry-live.json");
        File.WriteAllText(file, json);
        return file;
    }

    [Fact]
    public void Reader_keeps_guarding_states_even_in_the_release_window()
    {
        // Seleciona por IsGuarding: se selecionasse por IsBlocking, a janela de liberação derrubaria
        // o bloco inteiro e YouTube/Reddit vazariam no intervalo.
        var file = WriteState("""
            {"savedAtMs":1762882000000,"active":true,"locked":false,"endMs":1762882692123,
             "timer":{"phase":"Break","remainingMs":60000,"running":true}}
            """, out string directory);
        try
        {
            var state = AngryLiveStateReader.Read([file], Now);
            Assert.True(state.IsGuarding(Now));
            Assert.True(state.IsBreakRelease(Now));
            Assert.False(state.IsBlocking(Now));
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }

    [Fact]
    public void Reader_ignores_expired_sessions_and_unreadable_files()
    {
        var file = WriteState(
            """{"active":true,"locked":true,"endMs":1762882692123}""", out string directory);
        try
        {
            var afterEnd = Now.AddHours(2);
            Assert.False(AngryLiveStateReader.Read([file], afterEnd).IsGuarding(afterEnd));
            Assert.False(AngryLiveStateReader.Read(
                [Path.Combine(directory, "ausente.json")], Now).IsGuarding(Now));
            Assert.False(AngryLiveStateReader.Read([], Now).IsGuarding(Now));
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }
}

public sealed class AngryDistractionHostsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1762882000000);

    private static AngryLiveState Blocking() => new(true, true, Now.AddMinutes(10).ToUnixTimeMilliseconds());

    [Fact]
    public void Required_domains_are_listed()
    {
        foreach (var domain in new[]
                 {
                     "youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be",
                     "twitter.com", "www.twitter.com", "x.com", "www.x.com",
                     "instagram.com", "www.instagram.com",
                     "reddit.com", "www.reddit.com", "m.reddit.com",
                 })
        {
            Assert.Contains(domain, AngryDistractionHosts.Domains);
        }
    }

    [Fact]
    public void Blocking_state_adds_marked_section_for_every_domain()
    {
        var section = AngryDistractionHosts.BuildHostsSection(Blocking(), Now);
        Assert.Equal(AngryDistractionHosts.Domains.Count * 2 + 2, section.Count);
        Assert.Equal(AngryDistractionHosts.BeginMarker, section[0]);
        Assert.Equal(AngryDistractionHosts.EndMarker, section[^1]);
        Assert.Contains("0.0.0.0 youtube.com", section);
        Assert.Contains(":: youtube.com", section);
        Assert.Contains("0.0.0.0 x.com", section);
        Assert.Contains("0.0.0.0 instagram.com", section);
        Assert.Contains("0.0.0.0 reddit.com", section);
    }

    private static AngryLiveState Break(long remainingMs, bool running = true, string? phase = "Break") =>
        new(true, false, Now.AddMinutes(10).ToUnixTimeMilliseconds(), Now.ToUnixTimeMilliseconds(),
            phase, remainingMs, running);

    [Fact]
    public void Inactive_or_expired_state_adds_nothing()
    {
        Assert.Empty(AngryDistractionHosts.BuildHostsSection(AngryLiveState.Inactive, Now));
        Assert.Empty(AngryDistractionHosts.BuildHostsSection(new AngryLiveState(true, true, Now.AddMinutes(-1).ToUnixTimeMilliseconds()), Now));
        Assert.Empty(AngryDistractionHosts.BuildHostsSection(new AngryLiveState(true, true, null), Now));
    }

    [Fact]
    public void Break_without_live_timer_data_keeps_the_full_block()
    {
        // Intervalo sem timer.phase/running (ex.: esperando o Start manual): nada é liberado.
        var section = AngryDistractionHosts.BuildHostsSection(
            new AngryLiveState(true, false, Now.AddMinutes(5).ToUnixTimeMilliseconds()), Now);
        Assert.Equal(AngryDistractionHosts.Domains.Count * 2 + 2, section.Count);
        Assert.Contains("0.0.0.0 x.com", section);
        Assert.Contains("0.0.0.0 instagram.com", section);
    }

    [Fact]
    public void Break_release_omits_only_twitter_x_and_instagram()
    {
        var section = AngryDistractionHosts.BuildHostsSection(Break(60_000), Now);
        Assert.Equal(
            (AngryDistractionHosts.Domains.Count - AngryDistractionHosts.ReleasedDomains.Count) * 2 + 2,
            section.Count);
        Assert.Equal(AngryDistractionHosts.BeginMarker, section[0]);
        Assert.Equal(AngryDistractionHosts.EndMarker, section[^1]);

        foreach (var domain in AngryDistractionHosts.ReleasedDomains)
        {
            Assert.DoesNotContain("0.0.0.0 " + domain, section);
            Assert.DoesNotContain(":: " + domain, section);
        }

        // O resto continua bloqueado durante a liberação.
        Assert.Contains("0.0.0.0 youtube.com", section);
        Assert.Contains("0.0.0.0 youtu.be", section);
        Assert.Contains("0.0.0.0 reddit.com", section);
        Assert.Contains("0.0.0.0 m.reddit.com", section);
    }

    [Fact]
    public void Break_before_the_release_window_keeps_every_domain()
    {
        var section = AngryDistractionHosts.BuildHostsSection(Break(61_000), Now);
        Assert.Equal(AngryDistractionHosts.Domains.Count * 2 + 2, section.Count);
        Assert.Contains("0.0.0.0 x.com", section);
    }

    [Fact]
    public void Paused_break_keeps_every_domain()
    {
        var section = AngryDistractionHosts.BuildHostsSection(Break(30_000, running: false), Now);
        Assert.Equal(AngryDistractionHosts.Domains.Count * 2 + 2, section.Count);
        Assert.Contains("0.0.0.0 instagram.com", section);
    }

    [Fact]
    public void Stale_break_snapshot_returns_to_the_full_block_after_the_minute()
    {
        var state = Break(60_000);
        var staleNow = Now.AddSeconds(61);
        var section = AngryDistractionHosts.BuildHostsSection(state, staleNow);
        Assert.Equal(AngryDistractionHosts.Domains.Count * 2 + 2, section.Count);
        Assert.Contains("0.0.0.0 x.com", section);
        Assert.Contains("0.0.0.0 instagram.com", section);
    }

    [Fact]
    public void Released_domains_are_a_subset_of_domains()
    {
        Assert.NotEmpty(AngryDistractionHosts.ReleasedDomains);
        Assert.All(AngryDistractionHosts.ReleasedDomains,
            domain => Assert.Contains(domain, AngryDistractionHosts.Domains));
        Assert.DoesNotContain("youtube.com", AngryDistractionHosts.ReleasedDomains);
        Assert.DoesNotContain("reddit.com", AngryDistractionHosts.ReleasedDomains);
    }

    [Fact]
    public void Composing_without_the_angry_block_removes_it_and_keeps_the_original()
    {
        const string original = "127.0.0.1 localhost\n";
        var withAngry = HostsFile.Compose(original, AngryDistractionHosts.BuildSectionLines());
        Assert.Contains(AngryDistractionHosts.BeginMarker, withAngry);

        var removed = HostsFile.Compose(withAngry, Array.Empty<string>());
        Assert.DoesNotContain(AngryDistractionHosts.BeginMarker, removed);
        Assert.DoesNotContain("youtube.com", removed);
        Assert.Contains("127.0.0.1 localhost", removed);
    }
}

public sealed class DohResolversTests
{
    [Fact]
    public void Well_known_resolvers_are_listed()
    {
        foreach (var domain in new[]
                 {
                     "dns.google", "cloudflare-dns.com", "mozilla.cloudflare-dns.com",
                     "dns.quad9.net", "doh.opendns.com",
                 })
        {
            Assert.Contains(domain, DohResolvers.Domains);
        }
    }

    [Fact]
    public void Section_blocks_ipv4_and_ipv6_for_every_name()
    {
        var section = DohResolvers.BuildHostsSection();
        Assert.Equal(DohResolvers.Domains.Count * 2 + 2, section.Count);
        Assert.Equal(DohResolvers.BeginMarker, section[0]);
        Assert.Equal(DohResolvers.EndMarker, section[^1]);
        foreach (var domain in DohResolvers.Domains)
        {
            Assert.Contains("0.0.0.0 " + domain, section);
            Assert.Contains(":: " + domain, section);
        }
    }

    [Fact]
    public void Domains_are_unique_lowercase_and_look_like_hostnames()
    {
        Assert.Equal(DohResolvers.Domains.Distinct(StringComparer.Ordinal).Count(), DohResolvers.Domains.Count);
        Assert.All(DohResolvers.Domains, domain =>
        {
            Assert.Equal(domain.ToLowerInvariant(), domain);
            Assert.Contains('.', domain);
            Assert.DoesNotContain(' ', domain);
        });
    }
}

public sealed class AngryTitlesTests
{
    [Theory]
    [InlineData("YouTube - Google Chrome")]
    [InlineData("Home / X")]
    [InlineData("Fulano on X: \"oi\"")]
    [InlineData("Twitter / X - Microsoft Edge")]
    [InlineData("Instagram")]
    [InlineData("Instagram - Mozilla Firefox")]
    [InlineData("Reddit - Dive into anything")]
    [InlineData("r/brasil - Reddit")]
    public void Distraction_titles_are_detected(string title)
    {
        Assert.True(AngryTitles.IsDistractionTitle(title), title);
    }

    [Theory]
    [InlineData("Notepad")]
    [InlineData("Programação em C# - Microsoft Learn")]
    [InlineData("BBC News")]
    [InlineData("X - Files")]
    [InlineData("")]
    [InlineData(null)]
    public void Ordinary_titles_are_not_detected(string? title)
    {
        Assert.False(AngryTitles.IsDistractionTitle(title), title);
    }

    [Fact]
    public void Pathological_long_title_does_not_throw()
    {
        Assert.False(AngryTitles.IsDistractionTitle(new string('a', 200_000)));
        Assert.False(AngryTitles.IsReleasedTitle(new string('a', 200_000)));
    }

    [Theory]
    [InlineData("Home / X")]
    [InlineData("Fulano on X: \"oi\"")]
    [InlineData("Twitter / X - Microsoft Edge")]
    [InlineData("Instagram")]
    [InlineData("Instagram - Mozilla Firefox")]
    public void Released_titles_cover_only_twitter_x_and_instagram(string title)
    {
        Assert.True(AngryTitles.IsReleasedTitle(title), title);
        Assert.True(AngryTitles.IsDistractionTitle(title), title);
    }

    [Theory]
    [InlineData("YouTube - Google Chrome")]
    [InlineData("Reddit - Dive into anything")]
    [InlineData("r/brasil - Reddit")]
    [InlineData("Notepad")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_titles_are_not_released(string? title)
    {
        Assert.False(AngryTitles.IsReleasedTitle(title), title);
    }
}

public sealed class ForceUninstallGateTests
{
    private static (string Directory, string Config) CreateConfig(string password = "senha-de-teste-1")
    {
        var directory = TestPaths.NewDirectory();
        var config = Path.Combine(directory, "config.dat");
        PasswordConfig.Create(config, password);
        return (directory, config);
    }

    [Fact]
    public void Missing_password_is_rejected_with_the_auth_exit_code()
    {
        var (directory, config) = CreateConfig();
        try
        {
            var check = ForceUninstallGate.Check(null, config);
            Assert.False(check.Allowed);
            Assert.Equal(ForceUninstallGate.InvalidPasswordExitCode, check.ExitCode);
            Assert.Equal(LocalPasswordDecision.MissingPassword, check.Decision);
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }

    [Fact]
    public void Wrong_password_is_rejected_with_the_auth_exit_code()
    {
        var (directory, config) = CreateConfig();
        try
        {
            var check = ForceUninstallGate.Check("senha-errada", config);
            Assert.False(check.Allowed);
            Assert.Equal(ForceUninstallGate.InvalidPasswordExitCode, check.ExitCode);
            Assert.Equal(LocalPasswordDecision.WrongPassword, check.Decision);
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }

    [Fact]
    public void Correct_password_is_allowed()
    {
        var (directory, config) = CreateConfig();
        try
        {
            var check = ForceUninstallGate.Check("senha-de-teste-1", config);
            Assert.True(check.Allowed);
            Assert.Equal(0, check.ExitCode);
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }

    [Fact]
    public void Missing_config_is_rejected_with_exit_code_one()
    {
        var directory = TestPaths.NewDirectory();
        try
        {
            var check = ForceUninstallGate.Check("qualquer", Path.Combine(directory, "ausente.dat"));
            Assert.False(check.Allowed);
            Assert.Equal(1, check.ExitCode);
            Assert.Equal(LocalPasswordDecision.PasswordNotConfigured, check.Decision);
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }

    [Fact]
    public void Corrupted_config_is_rejected_with_exit_code_one()
    {
        var directory = TestPaths.NewDirectory();
        try
        {
            var config = Path.Combine(directory, "config.dat");
            File.WriteAllText(config, "lixo\n1\n2\n3\n");
            var check = ForceUninstallGate.Check("qualquer", config);
            Assert.False(check.Allowed);
            Assert.Equal(1, check.ExitCode);
            Assert.Equal(LocalPasswordDecision.PasswordUnreadable, check.Decision);
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }
}

public sealed class LocalPasswordCheckTests
{
    [Fact]
    public void Verifies_the_password_against_the_local_file()
    {
        var directory = TestPaths.NewDirectory();
        try
        {
            var config = Path.Combine(directory, "config.dat");
            PasswordConfig.Create(config, "senha-atual-1");
            Assert.True(LocalPasswordCheck.Verify("senha-atual-1", config).Allowed);
            Assert.False(LocalPasswordCheck.Verify("senha-atual-2", config).Allowed);
            Assert.False(LocalPasswordCheck.Verify(null, config).Allowed);
            Assert.False(LocalPasswordCheck.Verify("", config).Allowed);
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }
}

public sealed class SetupRunnerArgsTests
{
    [Fact]
    public void Decodes_base64_password_with_spaces()
    {
        var args = new[] { "--force-uninstall", "--silent", "--password", TestPaths.Encode("senha com espaço") };
        Assert.True(SetupRunner.TryDecodeOption(args, "--password", out string? value, out bool present));
        Assert.True(present);
        Assert.Equal("senha com espaço", value);
    }

    [Fact]
    public void Reports_missing_value_before_another_flag()
    {
        var args = new[] { "--force-uninstall", "--password", "--silent" };
        Assert.False(SetupRunner.TryDecodeOption(args, "--password", out string? value, out bool present));
        Assert.True(present);
        Assert.Null(value);
    }

    [Fact]
    public void Reports_invalid_base64()
    {
        var args = new[] { "--force-uninstall", "--password", "***" };
        Assert.False(SetupRunner.TryDecodeOption(args, "--password", out string? value, out bool present));
        Assert.True(present);
        Assert.Null(value);
    }

    [Fact]
    public void Absent_option_is_reported_as_absent()
    {
        var args = new[] { "--force-uninstall", "--silent" };
        Assert.False(SetupRunner.TryDecodeOption(args, "--password", out _, out bool present));
        Assert.False(present);
    }

    [Fact]
    public void Option_name_is_case_insensitive_and_decode_option_matches()
    {
        var args = new[] { "--PASSWORD", TestPaths.Encode("abc123") };
        Assert.Equal("abc123", SetupRunner.DecodeOption(args, "--password"));
        Assert.True(SetupRunner.HasOption(args, "--PASSWORD"));
    }
}

public sealed class SystemStateHostsTests
{
    private static (SystemState State, string Root) CreateState(
        Func<DateTimeOffset, IReadOnlyList<string>>? angry = null)
    {
        var root = TestPaths.NewDirectory();
        var state = new SystemState(
            new RecordingCommandRunner(),
            Path.Combine(root, "hosts"),
            Path.Combine(root, "hosts.original"),
            Path.Combine(root, "dns.dat"),
            Path.Combine(root, "policies.dat"),
            ["0.0.0.0 exemplo.com"],
            angry);
        return (state, root);
    }

    [Fact]
    public void Hosts_include_the_anti_doh_block_while_protected()
    {
        var (state, root) = CreateState();
        try
        {
            Assert.True(state.EnsureHosts());
            var hosts = File.ReadAllText(Path.Combine(root, "hosts"));
            Assert.Contains(HostsFile.BeginMarker, hosts);
            Assert.Contains("0.0.0.0 exemplo.com", hosts);
            Assert.Contains(DohResolvers.BeginMarker, hosts);
            Assert.Contains("0.0.0.0 dns.google", hosts);
            Assert.Contains(":: cloudflare-dns.com", hosts);

            // O backup guarda o original sem nenhum bloco nosso.
            var backup = File.ReadAllText(Path.Combine(root, "hosts.original"));
            Assert.DoesNotContain(DohResolvers.BeginMarker, backup);
        }
        finally
        {
            TestPaths.TryDelete(root);
        }
    }

    [Fact]
    public void Angry_block_is_added_while_requested_and_removed_after()
    {
        bool active = true;
        IReadOnlyList<string> Section(DateTimeOffset _) =>
            active ? AngryDistractionHosts.BuildSectionLines() : Array.Empty<string>();
        var (state, root) = CreateState(Section);
        try
        {
            var hostsPath = Path.Combine(root, "hosts");
            Assert.True(state.EnsureHosts());
            var withAngry = File.ReadAllText(hostsPath);
            Assert.Contains(AngryDistractionHosts.BeginMarker, withAngry);
            Assert.Contains("0.0.0.0 youtube.com", withAngry);

            active = false;
            Assert.True(state.EnsureHosts());
            var withoutAngry = File.ReadAllText(hostsPath);
            Assert.DoesNotContain(AngryDistractionHosts.BeginMarker, withoutAngry);
            Assert.DoesNotContain("youtube.com", withoutAngry);
            Assert.Contains(DohResolvers.BeginMarker, withoutAngry);
            Assert.Contains("0.0.0.0 exemplo.com", withoutAngry);
        }
        finally
        {
            TestPaths.TryDelete(root);
        }
    }

    [Fact]
    public void Angry_break_release_keeps_youtube_and_reddit_in_the_hosts_but_drops_twitter()
    {
        // O bloco continua aplicado no hosts durante a liberação: só as linhas de Twitter/X e
        // Instagram saem, senão YouTube/Reddit vazariam no intervalo.
        var fixedNow = DateTimeOffset.FromUnixTimeMilliseconds(1762882000000);
        var breakState = new AngryLiveState(
            true, false, fixedNow.AddMinutes(10).ToUnixTimeMilliseconds(),
            fixedNow.ToUnixTimeMilliseconds(), "Break", 60_000, true);
        var (state, root) = CreateState(_ => AngryDistractionHosts.BuildHostsSection(breakState, fixedNow));
        try
        {
            Assert.True(state.EnsureHosts());
            var hosts = File.ReadAllText(Path.Combine(root, "hosts"));
            Assert.Contains(AngryDistractionHosts.BeginMarker, hosts);
            Assert.Contains("0.0.0.0 youtube.com", hosts);
            Assert.Contains("0.0.0.0 reddit.com", hosts);
            Assert.DoesNotContain("0.0.0.0 x.com", hosts);
            Assert.DoesNotContain("0.0.0.0 instagram.com", hosts);
            Assert.Contains(DohResolvers.BeginMarker, hosts);
            Assert.Contains("0.0.0.0 exemplo.com", hosts);
        }
        finally
        {
            TestPaths.TryDelete(root);
        }
    }

    [Fact]
    public void Ensure_hosts_is_idempotent_when_nothing_changes()
    {
        var (state, root) = CreateState();
        try
        {
            var hostsPath = Path.Combine(root, "hosts");
            Assert.True(state.EnsureHosts());
            var first = File.ReadAllText(hostsPath);
            Assert.True(state.EnsureHosts());
            Assert.Equal(first, File.ReadAllText(hostsPath));
        }
        finally
        {
            TestPaths.TryDelete(root);
        }
    }
}

public sealed class ServiceWatchdogTests
{
    [Fact]
    public void Starts_a_stopped_service()
    {
        var runner = new RecordingCommandRunner { Result = new CommandResult(0, "STATE : 1  STOPPED") };
        Assert.True(ServiceWatchdog.EnsureRunning(runner, AppInfo.ServiceName));
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("start " + AppInfo.ServiceName, runner.Calls[1].Arguments);
    }

    [Fact]
    public void Does_not_start_a_running_service()
    {
        var runner = new RecordingCommandRunner { Result = new CommandResult(0, "STATE : 4  RUNNING") };
        Assert.True(ServiceWatchdog.EnsureRunning(runner, AppInfo.ServiceName));
        Assert.Single(runner.Calls);
    }

    [Fact]
    public void Waits_on_pending_states()
    {
        var runner = new RecordingCommandRunner { Result = new CommandResult(0, "STATE : 2  START_PENDING") };
        Assert.True(ServiceWatchdog.EnsureRunning(runner, AppInfo.ServiceName));
        Assert.Single(runner.Calls);
    }

    [Fact]
    public void Ignores_a_service_that_does_not_exist()
    {
        var runner = new RecordingCommandRunner { Result = new CommandResult(1060, "serviço não existe") };
        Assert.False(ServiceWatchdog.EnsureRunning(runner, AppInfo.WatchdogServiceName));
        Assert.Single(runner.Calls);
    }

    [Fact]
    public void Check_interval_is_five_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), ServiceWatchdog.CheckInterval);
    }

    [Fact]
    public void Recent_procedure_marker_blocks_supervision_and_old_marker_is_discarded()
    {
        var directory = TestPaths.NewDirectory();
        try
        {
            var marker = Path.Combine(directory, "install.lock");
            File.WriteAllText(marker, "instalando");
            Assert.True(ServiceWatchdog.IsProcedureInProgress(marker));

            File.SetLastWriteTimeUtc(marker, DateTime.UtcNow - TimeSpan.FromMinutes(11));
            Assert.False(ServiceWatchdog.IsProcedureInProgress(marker));
            Assert.False(File.Exists(marker));

            Assert.False(ServiceWatchdog.IsProcedureInProgress(Path.Combine(directory, "ausente.lock")));
        }
        finally
        {
            TestPaths.TryDelete(directory);
        }
    }
}

public sealed class VpnDetectorTests
{
    [Fact]
    public void Active_tunnel_is_detected()
    {
        var tunnel = new AdapterDnsInfo("t", "Cloudflare WARP", true, false, [], [], IsTunnel: true);
        var issues = VpnDetector.Detect([tunnel]);
        Assert.Single(issues);
        Assert.Contains("túnel", issues[0]);
    }

    [Fact]
    public void Loopback_dns_proxy_is_detected_on_a_normal_adapter()
    {
        var wifi = new AdapterDnsInfo("w", "Wi-Fi", true, true, ["127.0.2.2", "127.0.2.3"], []);
        var issues = VpnDetector.Detect([wifi]);
        Assert.Single(issues);
        Assert.Contains("DNS proxy local", issues[0]);
    }

    [Fact]
    public void Clean_adapter_and_down_tunnels_are_not_reported()
    {
        var ethernet = new AdapterDnsInfo("e", "Ethernet", true, true, ["1.1.1.1", "1.0.0.1"], []);
        var downTunnel = new AdapterDnsInfo("t", "Cloudflare WARP", false, false, [], [], IsTunnel: true);
        var downVpn = new AdapterDnsInfo("v", "Tailscale down", false, false, [], []);
        Assert.Empty(VpnDetector.Detect([ethernet, downTunnel, downVpn]));
    }

    [Fact]
    public void Known_vpn_name_on_a_non_tunnel_adapter_is_detected()
    {
        var adapter = new AdapterDnsInfo("v", "OpenVPN TAP-Windows6", true, true, ["1.1.1.1"], []);
        var issues = VpnDetector.Detect([adapter]);
        Assert.Single(issues);
    }

    [Fact]
    public void Has_loopback_dns_handles_ipv6()
    {
        var adapter = new AdapterDnsInfo("w", "Wi-Fi", true, true, [], ["::1"]);
        Assert.True(VpnDetector.HasLoopbackDns(adapter));
    }
}
