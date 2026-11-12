using System.IO.Pipes;
using System.Text;

namespace SysGuard;

/// <summary>
/// Teste interno do executável publicado (<c>SysGuard.exe --selftest</c>). Não altera nada no sistema:
/// valida lista, hosts, senha, políticas, títulos e o pipe de controle, e grava o resultado em
/// <c>%TEMP%\SysGuard-selftest.log</c>.
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        Log.Init(Path.Combine(Path.GetTempPath(), "SysGuard-selftest.log"));
        Log.Info($"Selftest do SysGuard versão {AppInfo.Version}.");
        int failures = 0;

        void Check(string name, Func<bool> test)
        {
            try
            {
                bool ok = test();
                Log.Info((ok ? "OK   " : "FAIL ") + name);
                if (!ok) failures++;
            }
            catch (Exception ex)
            {
                Log.Error("FAIL " + name, ex);
                failures++;
            }
        }

        Check("Lista de domínios embutida", () =>
        {
            var domains = BlockList.Domains;
            return domains.Count >= 1000
                   && domains.Contains("pornhub.com", StringComparer.OrdinalIgnoreCase)
                   && domains.Contains("xvideos.com", StringComparer.OrdinalIgnoreCase);
        });

        Check("Normalização de domínio", () =>
            BlockList.Normalize("0.0.0.0 Exemplo.COM") == "exemplo.com"
            && BlockList.Normalize("# comentário") is null
            && BlockList.Normalize("::1 localhost") is null
            && BlockList.Normalize("site") is null);

        Check("Hosts: montar e remover", () =>
        {
            const string original = "127.0.0.1 localhost\n";
            string[] section = ["0.0.0.0 exemplo.com", ":: exemplo.com"];
            var composed = HostsFile.Compose(original, section);
            var stripped = HostsFile.StripSection(composed);
            return HostsFile.HasSection(composed)
                   && composed.Contains("0.0.0.0 exemplo.com", StringComparison.Ordinal)
                   && stripped == original;
        });

        Check("Hosts: idempotente", () =>
        {
            string composed = HostsFile.Compose("127.0.0.1 localhost\n", ["0.0.0.0 a.com"]);
            string again = HostsFile.Compose(composed, ["0.0.0.0 a.com"]);
            return composed == again;
        });

        Check("Senha: hash e verificação", () =>
        {
            var record = PasswordHasher.Create("senha-forte-123");
            return PasswordHasher.Verify("senha-forte-123", record)
                   && !PasswordHasher.Verify("senha-forte-124", record)
                   && !PasswordHasher.Verify("", record);
        });

        Check("Senha: regras", () =>
            !PasswordHasher.IsAcceptable("12345", out _)
            && PasswordHasher.IsAcceptable("123456", out _)
            && PasswordHasher.IsAcceptable("com espaço", out _));

        Check("Senha: gravar e ler arquivo", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "SysGuard-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "config.dat");
                PasswordConfig.Create(path, "minha-senha-1");
                return PasswordConfig.Verify(path, "minha-senha-1")
                       && !PasswordConfig.Verify(path, "minha-senha-2");
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch { /* temp */ }
            }
        });

        Check("Limite de tentativas", () =>
        {
            var now = DateTimeOffset.UtcNow;
            var limiter = new AttemptLimiter(() => now);
            bool allowed = limiter.Check(out _) == AttemptDecision.Allowed;
            for (int i = 0; i < AttemptLimiter.MaxAttempts; i++) limiter.RecordFailure();
            bool locked = limiter.Check(out var remaining) == AttemptDecision.LockedOut && remaining > TimeSpan.Zero;
            now += AttemptLimiter.LockoutDuration + TimeSpan.FromSeconds(1);
            bool opened = limiter.Check(out _) == AttemptDecision.Allowed;
            return allowed && locked && opened;
        });

        Check("Títulos adultos", () =>
        {
            string[] positive =
            [
                "Pornhub - Google Chrome",
                "XVideos - Microsoft Edge",
                "Free Porn Videos - XVIDEOS.COM",
                "Chaturbate - Free Adult Webcams",
                "OnlyFans - Mozilla Firefox",
                "Vídeos de sexo amador - Opera",
                "Novinhas brasileiras - Google Chrome",
            ];
            string[] negative =
            [
                "Notepad",
                "YouTube - Google Chrome",
                "Instagram",
                "BBC News",
                "Sex Education - Netflix",
                "Essex - Wikipedia",
                "Programação em C# - Microsoft Learn",
            ];
            return positive.All(AdultContent.IsAdultTitle) && negative.All(title => !AdultContent.IsAdultTitle(title));
        });

        Check("Processos de navegador", () =>
            AdultContent.IsBrowserProcess("chrome")
            && AdultContent.IsBrowserProcess("Chrome.exe")
            && AdultContent.IsBrowserProcess("msedge")
            && AdultContent.IsBrowserProcess("firefox")
            && !AdultContent.IsBrowserProcess("notepad")
            && !AdultContent.IsBrowserProcess(""));

        Check("DNS: comparação normalizada", () =>
            DnsController.SameServers(
                ["185.228.168.10", "185.228.169.11"],
                DnsController.AdultV4)
            && DnsController.SameServers(["::ffff:1.1.1.1"], ["1.1.1.1"])
            && !DnsController.SameServers(["1.1.1.1"], ["1.0.0.1"]));

        Check("DNS: resolvedor Adult (sem YouTube restrito)", () =>
            DnsController.AdultV4Primary == "185.228.168.10"
            && DnsController.AdultV4Secondary == "185.228.169.11"
            && DnsController.AdultV6Primary == "2a0d:2a00:1::1"
            && DnsController.AdultV6Secondary == "2a0d:2a00:2::1"
            && !DnsController.AdultV4.Contains("185.228.168.168")
            && !DnsController.AdultV6.Contains("2a0d:2a00:1::")
            // Migração/reversão: os IPs do Family antigo continuam reconhecidos como "nossos".
            && DnsController.IsOurResolver("185.228.168.168")
            && DnsController.IsOurResolver("2a0d:2a00:2::")
            && !DnsController.IsOurResolver("1.1.1.1"));

        Check("Políticas: sem modo restrito do YouTube", () =>
            !BrowserPolicies.Desired.Any(p => p.ValueName == LegacyPolicies.YouTubeRestrictValue)
            && BrowserPolicies.Desired.Any(p =>
                p.KeyPath == @"SOFTWARE\Policies\Google\Chrome" && p.ValueName == "ForceGoogleSafeSearch")
            && BrowserPolicies.Desired.Any(p =>
                p.KeyPath == @"SOFTWARE\Policies\Microsoft\Edge" && p.ValueName == "ForceBingSafeSearch")
            && BrowserPolicies.Desired.Any(p => p.ValueName == "DnsOverHttpsMode" && p.StringValue == "off"));

        Check("Políticas: limpeza dos valores legados (simulada)", () =>
        {
            // Não toca no registro real: valida a mecânica de limpeza que a migração usa.
            var visited = 0;
            var removed = LegacyPolicies.RemoveAll(entry =>
            {
                visited++;
                return entry.Hive == Microsoft.Win32.RegistryHive.LocalMachine;
            });
            return visited == LegacyPolicies.Entries.Count
                   && removed.Count == 2
                   && removed.All(entry => entry.Hive == Microsoft.Win32.RegistryHive.LocalMachine);
        });

        Check("Políticas de navegador definidas", () =>
            BrowserPolicies.Desired.Count >= 10
            && BrowserPolicies.Desired.Any(p => p.KeyPath.Contains("Google\\Chrome", StringComparison.Ordinal))
            && BrowserPolicies.Desired.Any(p => p.KeyPath.Contains("Mozilla\\Firefox", StringComparison.Ordinal)));

        Check("Ícone da bandeja", () =>
        {
            using var icon = TrayIconFactory.Create();
            return icon.Width >= 16;
        });

        Check("Consulta de serviço", () =>
        {
            _ = ScService.IsInstalled();
            return true;
        });

        Check("Recuperação dos dois serviços (SCM)", () =>
        {
            var runner = new CapturingCommandRunner();
            var main = ScService.ConfigureRecovery(runner);
            var watchdog = ScService.ConfigureRecovery(runner, AppInfo.WatchdogServiceName);
            return main.Success && watchdog.Success
                   && runner.Calls.Count == 2
                   && runner.Calls[0].Arguments.Contains("failure SysGuardSvc", StringComparison.Ordinal)
                   && runner.Calls[1].Arguments.Contains("failure SysGuardWatchdogSvc", StringComparison.Ordinal)
                   && runner.Calls.All(call =>
                       call.Arguments.Contains("reset= 86400", StringComparison.Ordinal)
                       && call.Arguments.Contains("restart/5000/restart/10000/restart/30000", StringComparison.Ordinal));
        });

        Check("Serviços: principal e vigia distintos", () =>
            AppInfo.ServiceName != AppInfo.WatchdogServiceName
            && ScService.Create(new CapturingCommandRunner(), AppInfo.InstalledExePath).Success
            && AppInfo.ServiceRegistryPathFor(AppInfo.WatchdogServiceName)
                .EndsWith(AppInfo.WatchdogServiceName, StringComparison.Ordinal));

        Check("Vigia: reinicia serviço parado e não duplica o que já roda", () =>
        {
            var stoppedRunner = new CapturingCommandRunner
            {
                Responder = arguments => arguments.StartsWith("query ", StringComparison.Ordinal)
                    ? new CommandResult(0, "STATE : 1  STOPPED")
                    : new CommandResult(0, "OK"),
            };
            bool restarted = ServiceWatchdog.EnsureRunning(stoppedRunner, AppInfo.ServiceName);

            var runningRunner = new CapturingCommandRunner
            {
                Responder = arguments => arguments.StartsWith("query ", StringComparison.Ordinal)
                    ? new CommandResult(0, "STATE : 4  RUNNING")
                    : new CommandResult(0, "OK"),
            };
            bool stayed = ServiceWatchdog.EnsureRunning(runningRunner, AppInfo.ServiceName);

            return restarted && stayed
                   && stoppedRunner.Calls.Any(call => call.Arguments == "start " + AppInfo.ServiceName)
                   && !runningRunner.Calls.Any(call => call.Arguments.StartsWith("start ", StringComparison.Ordinal));
        });

        Check("Troca de senha: exige a senha atual", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "SysGuard-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string config = Path.Combine(directory, "config.dat");
                PasswordConfig.Create(config, "senha-atual-1");
                var wrong = LocalPasswordCheck.Verify("errada", config);
                var missing = LocalPasswordCheck.Verify(null, config);
                var right = LocalPasswordCheck.Verify("senha-atual-1", config);
                return !wrong.Allowed && !missing.Allowed && right.Allowed;
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch { /* temp */ }
            }
        });

        Check("VPN: detecção de túnel e DNS proxy local", () =>
        {
            var tunnel = new AdapterDnsInfo("tunnel", "Cloudflare WARP", true, false, [], [], IsTunnel: true);
            var wifiWarp = new AdapterDnsInfo("wifi", "Wi-Fi", true, true, ["127.0.2.2", "127.0.2.3"], []);
            var clean = new AdapterDnsInfo("eth", "Ethernet", true, true, ["1.1.1.1"], []);
            return VpnDetector.Detect([tunnel, wifiWarp]).Count >= 2
                   && VpnDetector.Detect([clean]).Count == 0;
        });

        Check("VPN: ambiente atual (diagnóstico)", () =>
        {
            try
            {
                var vpn = VpnDetector.Detect();
                if (vpn.Count > 0) Log.Info("AVISO (informativo): VPN/túnel no ambiente — " + string.Join("; ", vpn));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Selftest: detecção de VPN falhou.", ex);
                return true; // diagnóstico, não derruba o selftest
            }
        });

        Check("Remoção forçada: senha obrigatória", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "SysGuard-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string config = Path.Combine(directory, "config.dat");
                PasswordConfig.Create(config, "senha-de-teste-1");

                var missing = ForceUninstallGate.Check(null, config);
                var wrong = ForceUninstallGate.Check("errada", config);
                var right = ForceUninstallGate.Check("senha-de-teste-1", config);
                var absent = ForceUninstallGate.Check("senha-de-teste-1", Path.Combine(directory, "ausente.dat"));

                return !missing.Allowed && missing.ExitCode == ForceUninstallGate.InvalidPasswordExitCode
                       && !wrong.Allowed && wrong.ExitCode == ForceUninstallGate.InvalidPasswordExitCode
                       && right.Allowed && right.ExitCode == 0
                       && !absent.Allowed && absent.ExitCode != 0;
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch { /* temp */ }
            }
        });

        Check("Angry: parser do estado vivo", () =>
        {
            const string active = """
                {
                  "version": 1,
                  "savedAtUtc": "2026-11-11T17:14:32.1234567+00:00",
                  "savedAtMs": 1762881272123,
                  "active": true,
                  "locked": true,
                  "scheduled": false,
                  "phrase": "Eu desisto do meu foco agora.",
                  "endUtc": "2026-11-11T17:38:12.1234567+00:00",
                  "endMs": 1762882692123,
                  "timer": { "phase": "Focus", "remainingMs": 1420000, "running": true }
                }
                """;

            var duringFocus = DateTimeOffset.FromUnixTimeMilliseconds(1762882000000);
            var afterEnd = DateTimeOffset.FromUnixTimeMilliseconds(1762882692124);
            var state = AngryLiveStateParser.Parse(active);
            var breakBefore = AngryLiveStateParser.Parse("""
                {"savedAtMs":1762882000000,"active":true,"locked":false,"endMs":1762882692123,
                 "timer":{"phase":"Break","remainingMs":61000,"running":true}}
                """);
            var breakRelease = AngryLiveStateParser.Parse("""
                {"savedAtMs":1762882000000,"active":true,"locked":false,"endMs":1762882692123,
                 "timer":{"phase":"Break","remainingMs":60000,"running":true}}
                """);
            var breakPaused = AngryLiveStateParser.Parse("""
                {"savedAtMs":1762882000000,"active":true,"locked":false,"endMs":1762882692123,
                 "timer":{"phase":"Break","remainingMs":30000,"running":false}}
                """);

            return state.Active && state.Locked && state.EndUnixMs == 1762882692123
                   && state.TimerPhase == "Focus" && state.TimerRemainingMs == 1420000 && state.TimerRunning
                   && state.IsBlocking(duringFocus)
                   && !state.IsBlocking(afterEnd)
                   && breakBefore.TimerPhase == "Break" && breakBefore.TimerRunning
                   && breakBefore.IsGuarding(duringFocus) && !breakBefore.IsBreakRelease(duringFocus)
                   && breakBefore.IsBlocking(duringFocus)
                   && breakRelease.IsBreakRelease(duringFocus) && breakRelease.IsGuarding(duringFocus)
                   && !breakRelease.IsBlocking(duringFocus)
                   && breakPaused.IsGuarding(duringFocus) && !breakPaused.IsBreakRelease(duringFocus)
                   && breakPaused.IsBlocking(duringFocus)
                   && !breakRelease.IsBreakRelease(duringFocus.AddSeconds(61))
                   && breakRelease.IsBlocking(duringFocus.AddSeconds(61))
                   && AngryLiveStateParser.Parse("{\"active\":true,\"locked\":false,\"endMs\":1762882692123}").IsBlocking(duringFocus)
                   && !AngryLiveStateParser.Parse("{\"active\":false,\"locked\":true,\"endMs\":1762882692123}").IsBlocking(duringFocus)
                   && !AngryLiveStateParser.Parse("lixo").IsBlocking(duringFocus)
                   && AngryLiveStateParser.Parse("{\"Active\":true,\"Locked\":1,\"EndMs\":\"1762882692123\"}").IsBlocking(duringFocus);
        });

        Check("Angry: hosts de distração", () =>
        {
            var now = DateTimeOffset.FromUnixTimeMilliseconds(1762882000000);
            var activeState = AngryLiveStateParser.Parse("{\"active\":true,\"locked\":true,\"endMs\":1762882692123}");
            var section = AngryDistractionHosts.BuildHostsSection(activeState, now);
            if (section.Count == 0 || section[0] != AngryDistractionHosts.BeginMarker) return false;

            string composed = HostsFile.Compose("127.0.0.1 localhost\n", section);
            string removed = HostsFile.Compose(
                composed, AngryDistractionHosts.BuildHostsSection(AngryLiveState.Inactive, now));

            return composed.Contains("0.0.0.0 youtube.com", StringComparison.Ordinal)
                   && composed.Contains("0.0.0.0 youtu.be", StringComparison.Ordinal)
                   && composed.Contains("0.0.0.0 x.com", StringComparison.Ordinal)
                   && composed.Contains("0.0.0.0 instagram.com", StringComparison.Ordinal)
                   && composed.Contains("0.0.0.0 reddit.com", StringComparison.Ordinal)
                   && !removed.Contains(AngryDistractionHosts.BeginMarker, StringComparison.Ordinal)
                   && removed.Contains("0.0.0.0 exemplo.com", StringComparison.Ordinal) == false
                   && removed.Contains("127.0.0.1 localhost", StringComparison.Ordinal);
        });

        Check("Angry: liberação nos 60 s finais do intervalo", () =>
        {
            var now = DateTimeOffset.FromUnixTimeMilliseconds(1762882000000);
            var breakRelease = AngryLiveStateParser.Parse("""
                {"savedAtMs":1762882000000,"active":true,"locked":false,"endMs":1762882692123,
                 "timer":{"phase":"Break","remainingMs":60000,"running":true}}
                """);
            var breakBefore = AngryLiveStateParser.Parse("""
                {"savedAtMs":1762882000000,"active":true,"locked":false,"endMs":1762882692123,
                 "timer":{"phase":"Break","remainingMs":61000,"running":true}}
                """);
            var breakPaused = AngryLiveStateParser.Parse("""
                {"savedAtMs":1762882000000,"active":true,"locked":false,"endMs":1762882692123,
                 "timer":{"phase":"Break","remainingMs":30000,"running":false}}
                """);

            var releaseSection = AngryDistractionHosts.BuildHostsSection(breakRelease, now);
            var beforeSection = AngryDistractionHosts.BuildHostsSection(breakBefore, now);
            var pausedSection = AngryDistractionHosts.BuildHostsSection(breakPaused, now);
            var staleSection = AngryDistractionHosts.BuildHostsSection(breakRelease, now.AddSeconds(61));
            string releaseText = string.Join("\n", releaseSection);

            return breakRelease.IsGuarding(now) && breakRelease.IsBreakRelease(now)
                   && !breakRelease.IsBlocking(now)
                   && breakBefore.IsGuarding(now) && !breakBefore.IsBreakRelease(now)
                   && breakPaused.IsGuarding(now) && !breakPaused.IsBreakRelease(now)
                   && !breakRelease.IsBreakRelease(now.AddSeconds(61))
                   && releaseSection.Count == (AngryDistractionHosts.Domains.Count - AngryDistractionHosts.ReleasedDomains.Count) * 2 + 2
                   && releaseText.Contains("0.0.0.0 youtube.com", StringComparison.Ordinal)
                   && releaseText.Contains("0.0.0.0 reddit.com", StringComparison.Ordinal)
                   && !releaseText.Contains("0.0.0.0 x.com", StringComparison.Ordinal)
                   && !releaseText.Contains("0.0.0.0 instagram.com", StringComparison.Ordinal)
                   && beforeSection.Count == AngryDistractionHosts.Domains.Count * 2 + 2
                   && pausedSection.Count == AngryDistractionHosts.Domains.Count * 2 + 2
                   && staleSection.Count == AngryDistractionHosts.Domains.Count * 2 + 2;
        });

        Check("Anti-DoH: resolvedores no hosts", () =>
        {
            var section = DohResolvers.BuildHostsSection();
            return DohResolvers.Domains.Contains("dns.google", StringComparer.OrdinalIgnoreCase)
                   && DohResolvers.Domains.Contains("cloudflare-dns.com", StringComparer.OrdinalIgnoreCase)
                   && DohResolvers.Domains.Contains("mozilla.cloudflare-dns.com", StringComparer.OrdinalIgnoreCase)
                   && DohResolvers.Domains.Contains("dns.quad9.net", StringComparer.OrdinalIgnoreCase)
                   && DohResolvers.Domains.Contains("doh.opendns.com", StringComparer.OrdinalIgnoreCase)
                   && section.Contains("0.0.0.0 dns.google")
                   && section.Contains(":: dns.google")
                   && section[0] == DohResolvers.BeginMarker
                   && section[^1] == DohResolvers.EndMarker;
        });

        Check("Títulos de distração do Angry", () =>
            AngryTitles.IsDistractionTitle("YouTube - Google Chrome")
            && AngryTitles.IsDistractionTitle("Home / X")
            && AngryTitles.IsDistractionTitle("Fulano on X: \"oi\"")
            && AngryTitles.IsDistractionTitle("Instagram")
            && AngryTitles.IsDistractionTitle("r/brasil - Reddit")
            && !AngryTitles.IsDistractionTitle("Notepad")
            && !AngryTitles.IsDistractionTitle("Programação em C# - Microsoft Learn")
            && !AngryTitles.IsDistractionTitle("")
            && AngryTitles.IsReleasedTitle("Twitter / X - Microsoft Edge")
            && AngryTitles.IsReleasedTitle("Instagram - Mozilla Firefox")
            && !AngryTitles.IsReleasedTitle("YouTube - Google Chrome")
            && !AngryTitles.IsReleasedTitle("r/brasil - Reddit")
            && !AngryTitles.IsReleasedTitle("Notepad"));

        Check("Angry: caminhos por perfil de usuário", () =>
        {
            try
            {
                var files = AngryLivePaths.EnumerateUserFiles();
                return files.Count >= 1
                       && files.All(file => file.EndsWith(
                           Path.Combine(AngryLivePaths.AppFolderName, AngryLivePaths.FileName),
                           StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Error("Selftest: enumeração de perfis falhou.", ex);
                return false;
            }
        });

        Check("Pipe de controle (servidor + cliente)", () =>
        {
            if (TokenIntegrity.IsLowOrLess())
            {
                Log.Info("SKIP Pipe de controle: processo em integridade baixa (sandbox); executar o exe publicado fora do ambiente restrito.");
                return true;
            }

            return PipeRoundTrip();
        });

        Log.Info(failures == 0 ? "SELFTEST OK" : $"SELFTEST FALHOU ({failures} falha(s))");
        return failures == 0 ? 0 : 1;
    }

    private static bool PipeRoundTrip()
    {
        string pipeName = "SysGuard.Selftest." + Guid.NewGuid().ToString("N")[..10];
        using var server = new PipeServer(pipeName, request =>
        {

            return request == "PING" ? "OK" : "ERR";
        });
        server.Start();

        for (int attempt = 0; attempt < 25; attempt++)
        {
            if (SendToPipe(pipeName, "PING") == "OK") return true;
            Thread.Sleep(100);
        }

        Log.Info("Selftest pipe: servidor não respondeu ao cliente de teste.");
        return false;
    }

    private static string SendToPipe(string pipeName, string request)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            client.Connect(1000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
            writer.WriteLine(request);
            var readTask = Task.Run(() => reader.ReadLine());
            return readTask.Wait(3000) ? readTask.Result ?? string.Empty : "<timeout>";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Executor falso para validar os argumentos do sc.exe sem tocar nos serviços reais.</summary>
    private sealed class CapturingCommandRunner : ICommandRunner
    {
        public List<(string FileName, string Arguments)> Calls { get; } = [];

        public Func<string, CommandResult>? Responder { get; set; }

        public CommandResult Run(string fileName, string arguments, int timeoutMs = 30_000)
        {
            Calls.Add((fileName, arguments));
            return Responder?.Invoke(arguments) ?? new CommandResult(0, string.Empty);
        }
    }
}
