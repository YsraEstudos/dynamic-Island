using Microsoft.Win32;

namespace SysGuard;

/// <summary>
/// Coração do serviço: mantém a proteção aplicada em ciclo, atende os comandos da bandeja no pipe,
/// verifica a senha e executa a desinstalação. Todo o estado "desbloqueado" fica só na memória:
/// reiniciar o Windows (ou o serviço) volta ao estado protegido.
/// </summary>
internal sealed class GuardEngine : IDisposable
{
    private readonly string _configPath;
    private readonly AttemptLimiter _limiter;
    private readonly Func<bool> _applyProtection;
    private readonly Func<bool> _removeProtection;
    private readonly Func<bool> _applyHostsAndPolicies;
    private readonly Func<IReadOnlyList<string>> _detectVpn;
    private readonly object _gate = new();
    private readonly object _applyGate = new();
    private System.Threading.Timer? _timer;
    private PipeServer? _pipe;
    private int _tickBusy;
    private int _tickCount;
    private bool _unlocked;
    private bool _stopping;
    private bool _uninstalling;
    private volatile bool _lastApplyOk;
    private string? _lastVpnSignature;

    public GuardEngine(
        SystemState state,
        string configPath,
        AttemptLimiter? limiter = null,
        Func<bool>? applyProtection = null,
        Func<bool>? removeProtection = null,
        Func<bool>? applyHostsAndPolicies = null,
        Func<IReadOnlyList<string>>? detectVpn = null)
    {
        _configPath = configPath;
        _limiter = limiter ?? new AttemptLimiter();
        _applyProtection = applyProtection ?? state.ApplyProtection;
        _removeProtection = removeProtection ?? state.RemoveProtection;
        _applyHostsAndPolicies = applyHostsAndPolicies ?? state.ApplyHostsAndPolicies;
        _detectVpn = detectVpn ?? VpnDetector.Detect;
    }

    /// <summary>True quando a proteção está ativa (não desbloqueada).</summary>
    public bool IsProtected
    {
        get { lock (_gate) return !_unlocked; }
    }

    /// <summary>True quando a última aplicação das camadas foi completa.</summary>
    public bool IsHealthy => _lastApplyOk;

    /// <summary>Aplica a proteção (em segundo plano) e começa o watchdog e o pipe de controle.</summary>
    public bool Start()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                Log.Error("Serviço iniciado sem arquivo de senha; configuração incompleta.");
                return false;
            }

            _pipe = new PipeServer(AppInfo.PipeName, HandleRequest);
            _pipe.Start();
            _timer = new System.Threading.Timer(_ => Tick(), null, dueTime: 0, period: 3000);

            // A aplicação inicial roda fora da thread do SCM: o serviço reporta RUNNING rápido e o
            // instalador espera o status saudável antes de considerar a instalação concluída.
            _ = Task.Run(ApplyInitialAsync);

            Log.Info($"Serviço iniciado. Versão {AppInfo.Version}.");
            var vpn = SafeDetectVpn();
            if (vpn.Count > 0)
                Log.Info("Atenção: VPN/túnel ativo no sistema pode furar o DNS do filtro adulto — " + string.Join("; ", vpn));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao iniciar o serviço.", ex);
            return false;
        }
    }

    private void ApplyInitialAsync()
    {
        try
        {
            lock (_applyGate)
            {
                lock (_gate)
                {
                    if (_stopping || _unlocked) return;
                }

                SetHealth(_applyProtection());
                if (_lastApplyOk) Log.Info("Proteção inicial aplicada.");
                else Log.Info("Proteção inicial parcial.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha na aplicação inicial da proteção.", ex);
        }
    }

    /// <summary>Para o watchdog e o pipe e devolve o sistema ao estado original (parada limpa = desbloqueio).</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_stopping) return;
            _stopping = true;
            _unlocked = true;
        }

        DisposeResources();
        if (_uninstalling)
        {
            // A restauração já foi feita (e a limpeza está agendada) no comando de desinstalação.
            return;
        }

        try
        {
            lock (_applyGate)
            {
                bool ok = _removeProtection();
                Log.Info(ok ? "Serviço parado; proteção removida." : "Serviço parado; restauração parcial.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao restaurar o sistema ao parar o serviço.", ex);
        }
    }

    public void Dispose() => DisposeResources();

    private void DisposeResources()
    {
        var timer = _timer;
        if (timer is not null)
        {
            timer.Dispose();
            _timer = null;
        }

        _pipe?.Dispose();
        _pipe = null;
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _tickBusy, 1) != 0) return;
        try
        {
            lock (_applyGate)
            {
                lock (_gate)
                {
                    if (_stopping || _unlocked) return;
                }

                _tickCount++;
                if (_tickCount % 5 == 0)
                {
                    // Só a checagem completa (com DNS) pode declarar a proteção saudável de novo.
                    SetHealth(_applyProtection());
                }
                else if (!_applyHostsAndPolicies())
                {
                    SetHealth(false);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Watchdog falhou.", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _tickBusy, 0);
        }
    }

    /// <summary>Atende uma linha de requisição do pipe (ver <see cref="PipeClient"/>).</summary>
    internal string HandleRequest(string request)
    {
        if (string.IsNullOrWhiteSpace(request)) return "ERR EMPTY";

        var space = request.IndexOf(' ');
        var command = (space < 0 ? request : request[..space]).Trim().ToUpperInvariant();
        var payload = space < 0 ? string.Empty : request[(space + 1)..];

        try
        {
            return command switch
            {
                "PING" => "OK",
                "VERSION" => "OK " + AppInfo.Version,
                "STATUS" => StatusResponse(),
                "VPN" => VpnResponse(),
                "LOCK" => Lock(),
                "UNLOCK" => Unlock(payload),
                "CHANGE" => ChangePassword(payload),
                "UNINSTALL" => Uninstall(payload),
                _ => "ERR UNKNOWN",
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Comando '{command}' falhou.", ex);
            return "ERR INTERNAL";
        }
    }

    /// <summary>Atualiza a saúde só quando muda; a checagem barata pode piorar, nunca melhorar sozinha.</summary>
    private void SetHealth(bool ok)
    {
        if (ok == _lastApplyOk) return;
        _lastApplyOk = ok;
        Log.Info(ok ? "Watchdog: proteção confirmada." : "Watchdog: proteção divergente (tentando de novo).");
    }

    private string StatusResponse()
    {
        string status;
        lock (_gate)
        {
            status = _unlocked ? "OK UNPROTECTED"
                : _lastApplyOk ? "OK PROTECTED"
                : "OK PROTECTED DEGRADED";
        }

        if (status == "OK UNPROTECTED") return status;

        // PC-17: VPN/túnel ativo não impede as camadas de serem aplicadas, mas pode furar o DNS
        // filtro adulto. O status sinaliza e o log registra quando o conjunto detectado muda; derrubar a
        // VPN não é decisão do app (quebraria o uso e exigiria admin).
        var vpn = SafeDetectVpn();
        if (vpn.Count == 0)
        {
            _lastVpnSignature = null;
            return status;
        }

        var signature = string.Join("; ", vpn);
        if (!string.Equals(signature, _lastVpnSignature, StringComparison.Ordinal))
        {
            _lastVpnSignature = signature;
            Log.Info("Atenção: VPN/túnel ativo pode furar o filtro de DNS — " + signature);
        }

        return status + " VPN";
    }

    /// <summary>Diagnóstico: VPN/túnel ativos detectados ("OK NONE" quando nada).</summary>
    private string VpnResponse()
    {
        var vpn = SafeDetectVpn();
        return vpn.Count == 0 ? "OK NONE" : "OK " + string.Join(" | ", vpn);
    }

    private IReadOnlyList<string> SafeDetectVpn()
    {
        try
        {
            return _detectVpn();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao detectar VPN/túnel.", ex);
            return Array.Empty<string>();
        }
    }

    private string Lock()
    {
        if (IsProtected) return "OK";

        lock (_gate) _unlocked = false;
        bool ok;
        lock (_applyGate) ok = _applyProtection();
        SetHealth(ok);
        Log.Info("Proteção reativada pela bandeja.");
        return ok ? "OK" : "OK PARTIAL";
    }

    private string Unlock(string encodedPassword)
    {
        string auth = Authenticate(encodedPassword, out _);
        if (auth != "OK") return auth;
        if (!IsProtected) return "OK";

        lock (_gate) _unlocked = true;

        bool restored;
        lock (_applyGate) restored = _removeProtection();
        if (!restored)
        {
            // Não desbloqueia "pela metade": volta a proteger e informa a falha.
            lock (_gate) _unlocked = false;
            lock (_applyGate) SetHealth(_applyProtection());
            Log.Error("Desbloqueio abortado: restauração falhou; proteção reaplicada.");
            return "ERR RESTORE";
        }

        SetHealth(true);
        Log.Info("Proteção desativada com senha.");
        return "OK";
    }

    private string ChangePassword(string payload)
    {
        var fields = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 2) return "ERR BADREQUEST";

        var current = Decode(fields[0]);
        var next = Decode(fields[1]);
        string auth = Authenticate(EncodeForAuth(current), out _);
        if (auth != "OK") return auth;
        if (!PasswordHasher.IsAcceptable(next, out _)) return "ERR WEAK";

        PasswordConfig.Create(_configPath, next);
        Log.Info("Senha alterada.");
        return "OK";
    }

    private string Uninstall(string encodedPassword)
    {
        string auth = Authenticate(encodedPassword, out _);
        if (auth != "OK") return auth;

        lock (_gate)
        {
            _uninstalling = true;
            _unlocked = true;
        }

        bool restored;
        lock (_applyGate) restored = _removeProtection();
        if (!restored)
        {
            lock (_gate)
            {
                _uninstalling = false;
                _unlocked = false;
            }

            lock (_applyGate) SetHealth(_applyProtection());
            Log.Error("Desinstalação abortada: restauração falhou; proteção reaplicada.");
            return "ERR RESTORE";
        }

        try
        {
            RemoveRunKey();
            File.WriteAllText(AppInfo.PendingDeletePath, "pending\n");

            if (!ServiceInstaller.SpawnUninstallCleanup(new ProcessCommandRunner()))
            {
                Log.Error("Desinstalação: limpeza não agendada; marcador mantido para o próximo boot.");
                return "ERR CLEANUP";
            }

            Log.Info("Desinstalação solicitada; limpeza agendada.");
            return "OK";
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao preparar a desinstalação.", ex);
            return "ERR CLEANUP";
        }
    }

    private static void RemoveRunKey()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(AppInfo.RunKeyPath, writable: true);
            key?.DeleteValue(AppInfo.RunValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao remover o valor de início automático.", ex);
        }
    }

    /// <summary>Verifica a senha com limite de tentativas. Devolve "OK", "ERR AUTH n" ou "ERR LOCKOUT s".</summary>
    private string Authenticate(string encodedPassword, out string password)
    {
        password = Decode(encodedPassword);
        var decision = _limiter.Check(out var remaining);
        if (decision == AttemptDecision.LockedOut)
            return "ERR LOCKOUT " + Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));

        if (!PasswordConfig.Verify(_configPath, password))
        {
            int left = _limiter.RecordFailure();
            Log.Info($"Senha incorreta ({AttemptLimiter.MaxAttempts - left}/{AttemptLimiter.MaxAttempts} tentativas usadas).");
            return "ERR AUTH " + left;
        }

        _limiter.RecordSuccess();
        return "OK";
    }

    private static string EncodeForAuth(string password) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password));

    internal static string Decode(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return string.Empty;
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }
}
