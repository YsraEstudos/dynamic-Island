namespace SysGuard.Tests;

using SysGuard;

public sealed class AttemptLimiterTests
{
    [Fact]
    public void Five_failures_lock_for_a_minute_then_open_again()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var limiter = new AttemptLimiter(() => now);

        Assert.Equal(AttemptDecision.Allowed, limiter.Check(out _));
        Assert.Equal(4, limiter.RecordFailure());
        Assert.Equal(3, limiter.RecordFailure());
        Assert.Equal(2, limiter.RecordFailure());
        Assert.Equal(1, limiter.RecordFailure());
        Assert.Equal(0, limiter.RecordFailure());

        Assert.Equal(AttemptDecision.LockedOut, limiter.Check(out var remaining));
        Assert.True(remaining > TimeSpan.Zero);

        now += TimeSpan.FromSeconds(30);
        Assert.Equal(AttemptDecision.LockedOut, limiter.Check(out _));

        now += TimeSpan.FromSeconds(31);
        Assert.Equal(AttemptDecision.Allowed, limiter.Check(out _));
    }

    [Fact]
    public void Success_resets_the_counter()
    {
        var limiter = new AttemptLimiter();
        limiter.RecordFailure();
        limiter.RecordFailure();
        limiter.RecordSuccess();
        Assert.Equal(4, limiter.RecordFailure());
    }
}

public sealed class GuardEngineTests
{
    // Aplicadores falsos: os testes de engine nunca tocam no registro/DNS/hosts reais da máquina.
    private static (GuardEngine Engine, string ConfigPath, string Root) CreateEngine(
        Func<DateTimeOffset>? clock = null,
        Func<bool>? apply = null,
        Func<bool>? remove = null,
        Func<bool>? applyPartial = null,
        Func<IReadOnlyList<string>>? vpn = null)
    {
        var root = TestPaths.NewDirectory();
        var runner = new RecordingCommandRunner();
        var state = new SystemState(
            runner,
            Path.Combine(root, "hosts"),
            Path.Combine(root, "hosts.original"),
            Path.Combine(root, "dns.dat"),
            Path.Combine(root, "policies.dat"),
            ["0.0.0.0 exemplo.com"]);

        var configPath = Path.Combine(root, "config.dat");
        PasswordConfig.Create(configPath, "senha-de-teste");
        var engine = new GuardEngine(
            state,
            configPath,
            new AttemptLimiter(clock),
            apply ?? (() => true),
            remove ?? (() => true),
            applyPartial ?? (() => true),
            // Testes determinísticos: a máquina de teste pode ter VPN/WARP ativa e o status mudaria.
            vpn ?? (() => Array.Empty<string>()));
        return (engine, configPath, root);
    }

    [Fact]
    public void Basic_commands_work()
    {
        var (engine, _, root) = CreateEngine();
        try
        {
            Assert.Equal("OK", engine.HandleRequest("PING"));
            Assert.StartsWith("OK ", engine.HandleRequest("VERSION"));
            Assert.StartsWith("OK PROTECTED", engine.HandleRequest("STATUS"));
            Assert.Equal("ERR UNKNOWN", engine.HandleRequest("QUALQUERCOISA"));
            Assert.Equal("ERR EMPTY", engine.HandleRequest("   "));
        }
        finally
        {
            engine.Dispose();
            TestPaths.TryDelete(root);
        }
    }

    [Fact]
    public void Wrong_password_is_rejected_and_attempts_are_reported()
    {
        var (engine, _, root) = CreateEngine();
        try
        {
            Assert.Equal("ERR AUTH 4", engine.HandleRequest("UNLOCK " + TestPaths.Encode("errada")));
            Assert.Equal("ERR AUTH 3", engine.HandleRequest("UNLOCK " + TestPaths.Encode("outra-errada")));
            Assert.StartsWith("OK PROTECTED", engine.HandleRequest("STATUS"));
        }
        finally
        {
            engine.Dispose();
            TestPaths.TryDelete(root);
        }
    }

    [Fact]
    public void Correct_password_unlocks_and_lock_returns_to_protected()
    {
        var (engine, _, root) = CreateEngine();
        try
        {
            Assert.Equal("OK", engine.HandleRequest("UNLOCK " + TestPaths.Encode("senha-de-teste")));
            Assert.Equal("OK UNPROTECTED", engine.HandleRequest("STATUS"));

            Assert.Equal("OK", engine.HandleRequest("LOCK"));
            Assert.Equal("OK PROTECTED", engine.HandleRequest("STATUS"));
        }
        finally
        {
            engine.Dispose();
            TestPaths.TryDelete(root);
        }
    }

    [Fact]
    public void Lockout_after_five_failures_blocks_even_the_right_password()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var (engine, _, root) = CreateEngine(() => now);
        try
        {
            for (int i = 0; i < 5; i++)
                Assert.StartsWith("ERR AUTH", engine.HandleRequest("UNLOCK " + TestPaths.Encode("errada")));

            Assert.StartsWith("ERR LOCKOUT", engine.HandleRequest("UNLOCK " + TestPaths.Encode("senha-de-teste")));

            now += TimeSpan.FromSeconds(61);
            Assert.Equal("OK", engine.HandleRequest("UNLOCK " + TestPaths.Encode("senha-de-teste")));
        }
        finally
        {
            engine.Dispose();
            TestPaths.TryDelete(root);
        }
    }

    [Fact]
    public void Change_password_requires_current_password()
    {
        var (engine, configPath, root) = CreateEngine();
        try
        {
            Assert.StartsWith("ERR AUTH", engine.HandleRequest(
                "CHANGE " + TestPaths.Encode("errada") + " " + TestPaths.Encode("senha-nova-1")));
            Assert.True(PasswordConfig.Verify(configPath, "senha-de-teste"));

            Assert.Equal("OK", engine.HandleRequest(
                "CHANGE " + TestPaths.Encode("senha-de-teste") + " " + TestPaths.Encode("senha-nova-1")));
            Assert.True(PasswordConfig.Verify(configPath, "senha-nova-1"));
            Assert.False(PasswordConfig.Verify(configPath, "senha-de-teste"));

            Assert.Equal("OK", engine.HandleRequest("UNLOCK " + TestPaths.Encode("senha-nova-1")));
        }
        finally
        {
            engine.Dispose();
            TestPaths.TryDelete(root);
        }
    }

    [Fact]
    public void Status_reports_degraded_when_application_fails()
    {
        var (engine, _, root) = CreateEngine(apply: () => false);
        try
        {
            Assert.Equal("OK", engine.HandleRequest("UNLOCK " + TestPaths.Encode("senha-de-teste")));
            Assert.Equal("OK UNPROTECTED", engine.HandleRequest("STATUS"));
            Assert.Equal("OK PARTIAL", engine.HandleRequest("LOCK"));
            Assert.Equal("OK PROTECTED DEGRADED", engine.HandleRequest("STATUS"));
        }
        finally
        {
            engine.Dispose();
            TestPaths.TryDelete(root);
        }
    }

    [Fact]
    public void Unlock_is_aborted_if_restore_fails()
    {
        var (engine, _, root) = CreateEngine(remove: () => false);
        try
        {
            Assert.Equal("ERR RESTORE", engine.HandleRequest("UNLOCK " + TestPaths.Encode("senha-de-teste")));
            Assert.Equal("OK PROTECTED", engine.HandleRequest("STATUS"));
        }
        finally
        {
            engine.Dispose();
            TestPaths.TryDelete(root);
        }
    }

    [Fact]
    public void Change_password_rejects_weak_new_password()
    {
        var (engine, configPath, root) = CreateEngine();
        try
        {
            Assert.Equal("ERR WEAK", engine.HandleRequest(
                "CHANGE " + TestPaths.Encode("senha-de-teste") + " " + TestPaths.Encode("123")));
            Assert.True(PasswordConfig.Verify(configPath, "senha-de-teste"));
        }
        finally
        {
            engine.Dispose();
            TestPaths.TryDelete(root);
        }
    }

    [Fact]
    public void Status_appends_vpn_suffix_but_keeps_health_code()
    {
        var (engine, _, root) = CreateEngine(vpn: () => ["adaptador de túnel ativo (Cloudflare WARP)"]);
        try
        {
            // Sem Start() o engine ainda não confirmou a aplicação: continua DEGRADED, com o sufixo VPN.
            Assert.Equal("OK PROTECTED DEGRADED VPN", engine.HandleRequest("STATUS"));
            Assert.Equal("OK PROTECTED DEGRADED VPN", engine.HandleRequest("STATUS"));
        }
        finally
        {
            engine.Dispose();
            TestPaths.TryDelete(root);
        }
    }

    [Fact]
    public void Vpn_command_reports_detected_tunnels()
    {
        var (clean, _, cleanRoot) = CreateEngine();
        var (tunnel, _, tunnelRoot) = CreateEngine(vpn: () => ["adaptador de túnel ativo (Tailscale)"]);
        try
        {
            Assert.Equal("OK NONE", clean.HandleRequest("VPN"));
            Assert.Equal("OK adaptador de túnel ativo (Tailscale)", tunnel.HandleRequest("VPN"));
        }
        finally
        {
            clean.Dispose();
            tunnel.Dispose();
            TestPaths.TryDelete(cleanRoot);
            TestPaths.TryDelete(tunnelRoot);
        }
    }
}
