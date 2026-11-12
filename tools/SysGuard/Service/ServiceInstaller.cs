using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace SysGuard;

internal sealed record InstallOutcome(bool Success, string? Error)
{
    public static InstallOutcome Ok() => new(true, null);
    public static InstallOutcome Fail(string error) => new(false, error);
}

/// <summary>
/// Instalação, reparo e limpeza da desinstalação. Roda com privilégios de administrador
/// (janela de setup elevada); o serviço em si usa apenas a parte de limpeza agendada.
/// </summary>
internal static class ServiceInstaller
{
    private static readonly ICommandRunner Runner = new ProcessCommandRunner();

    public static bool IsInstalled => ScService.IsInstalled();

    public static bool IsRunning => ScService.IsRunning(Runner);

    /// <summary>
    /// Instala (senha nova) ou repara (senha nula = mantém a existente) a proteção.
    /// </summary>
    public static InstallOutcome Install(string sourceExePath, string? newPassword)
    {
        SystemState? state = null;
        bool wasInstalled = ScService.IsInstalled();
        InstallOutcome Fail(string error)
        {
            Log.Error("Instalação: " + error);
            Rollback(state, wasInstalled);
            TryDeleteFile(AppInfo.InstallLockPath);
            return InstallOutcome.Fail(error);
        }

        try
        {
            Log.Info($"Instalação: iniciando (reparo={wasInstalled}).");

            // Marcador de desinstalação antiga não pode auto-desinstalar uma instalação nova.
            TryDeleteFile(AppInfo.PendingDeletePath);
            // Enquanto o reparo para os serviços, o vigia não pode religar um ao outro.
            WriteInstallLock();

            if (wasInstalled && !StopExistingService())
                return Fail("O serviço anterior não parou; a instalação foi abortada para não substituir um exe em uso.");

            if (PrepareTrustedDirectories(fresh: !wasInstalled) is { } directoryError) return Fail(directoryError);
            if (CopyExecutable(sourceExePath) is { } copyError) return Fail(copyError);
            if (!ApplyAcls()) return Fail("Não foi possível proteger as permissões da pasta de instalação.");

            state = SystemState.CreateDefault();
            if (!HasCompleteState())
            {
                if (state.IsHostsProtected() || File.Exists(AppInfo.HostsBackupPath)
                    || File.Exists(AppInfo.PoliciesStatePath) || File.Exists(AppInfo.DnsStatePath))
                {
                    state.EmergencyReset();
                }

                state.Snapshot();
                if (!HasCompleteState())
                    return Fail("Não foi possível guardar o estado original do sistema (hosts/DNS/políticas).");
            }

            if (newPassword is not null)
            {
                if (!PasswordHasher.IsAcceptable(newPassword, out var passwordError))
                    return Fail(passwordError ?? "Senha inválida.");
                PasswordConfig.Create(AppInfo.ConfigPath, newPassword);
            }

            if (!File.Exists(AppInfo.ConfigPath))
                return Fail("Nenhuma senha definida. Execute a instalação com uma senha nova.");

            File.WriteAllText(
                AppInfo.InstallStatePath,
                $"version={AppInfo.Version}\ninstalled={DateTimeOffset.Now:O}\n",
                new UTF8Encoding(false));

            var create = ScService.Create(Runner, AppInfo.InstalledExePath);
            if (!create.Success)
                return Fail("Não foi possível criar o serviço: " + create.Output);

            if (!ScService.EnsureImagePath(AppInfo.InstalledExePath, out var imagePathError))
                return Fail("Não foi possível ajustar o caminho do serviço: " + imagePathError);

            var recovery = ScService.ConfigureRecovery(Runner);
            if (!recovery.Success)
                Log.Error("Instalação: não foi possível configurar a recuperação do serviço: " + recovery.Output);
            ScService.SetDescription(Runner);

            // Serviço gêmeo: reinicia o principal quando ele para; o principal também vigia este.
            var createWatchdog = ScService.CreateWatchdog(Runner, AppInfo.InstalledExePath);
            if (!createWatchdog.Success)
                return Fail("Não foi possível criar o serviço vigia: " + createWatchdog.Output);

            if (!ScService.EnsureWatchdogImagePath(AppInfo.InstalledExePath, out var watchdogImageError))
                return Fail("Não foi possível ajustar o caminho do serviço vigia: " + watchdogImageError);

            var watchdogRecovery = ScService.ConfigureRecovery(Runner, AppInfo.WatchdogServiceName);
            if (!watchdogRecovery.Success)
                Log.Error("Instalação: recuperação do serviço vigia: " + watchdogRecovery.Output);
            ScService.SetWatchdogDescription(Runner);

            var start = ScService.Start(Runner);
            if (!ScService.WaitForState(Runner, running: true, TimeSpan.FromSeconds(25)))
                return Fail("O serviço não iniciou: " + start.Output);

            if (!WaitForPipe(TimeSpan.FromSeconds(20)))
                return Fail("O serviço iniciou, mas não respondeu ao controle. Diagnóstico em %TEMP%\\SysGuard.log e %TEMP%\\SysGuard-service.log.");

            if (!WaitForHealthy(TimeSpan.FromSeconds(25)))
                return Fail("O serviço iniciou, mas não confirmou as camadas de bloqueio.");

            var watchStart = ScService.Start(Runner, AppInfo.WatchdogServiceName);
            if (!ScService.WaitForState(Runner, running: true, TimeSpan.FromSeconds(20), AppInfo.WatchdogServiceName))
                return Fail("O serviço vigia não iniciou: " + watchStart.Output);

            WriteRunKey();
            TryDeleteFile(AppInfo.InstallLockPath);
            Log.Info("Instalação: concluída (serviço principal + vigia).");
            return InstallOutcome.Ok();
        }
        catch (Exception ex)
        {
            Log.Error("Instalação falhou.", ex);
            Rollback(state, wasInstalled);
            TryDeleteFile(AppInfo.InstallLockPath);
            return InstallOutcome.Fail(ex.Message);
        }
    }

    /// <summary>Remove a proteção e os dois serviços mesmo sem o serviço responder (via setup elevado).</summary>
    public static InstallOutcome ForceUninstall()
    {
        try
        {
            Log.Info("Desinstalação forçada: iniciando.");
            WriteInstallLock();
            // O vigia para primeiro: se ficasse vivo, reiniciaria o principal no meio da remoção.
            StopExistingService();

            var delete = ScService.Delete(Runner);
            var deleteWatchdog = ScService.Delete(Runner, AppInfo.WatchdogServiceName);
            bool servicesGone = !ScService.IsInstalled() && !ScService.IsInstalled(AppInfo.WatchdogServiceName);
            if (!servicesGone)
            {
                Log.Error("Desinstalação forçada: serviço ainda registrado: " + delete.Output + " / " + deleteWatchdog.Output);
                return InstallOutcome.Fail(
                    "Os serviços não puderam ser removidos agora. Reinicie o computador e execute a remoção forçada de novo.");
            }

            // sc delete marca remoção, mas o processo pode continuar rodando até parar/reiniciar.
            if (PipeClient.Send("PING", 1500) == "OK")
            {
                Log.Error("Desinstalação forçada: o processo do serviço ainda está em execução.");
                return InstallOutcome.Fail(
                    "O serviço ainda está em execução. Reinicie o computador e execute a remoção forçada de novo.");
            }

            DeleteRunKey();
            SystemState.CreateDefault().EmergencyReset();
            if (!TryDeleteDirectory(AppInfo.ProgramDataDirectory))
                Log.Info("Desinstalação forçada: a pasta será removida em um próximo boot.");

            return InstallOutcome.Ok();
        }
        catch (Exception ex)
        {
            Log.Error("Desinstalação forçada falhou.", ex);
            return InstallOutcome.Fail(ex.Message);
        }
        finally
        {
            TryDeleteFile(AppInfo.InstallLockPath);
        }
    }

    /// <summary>
    /// Agenda a limpeza que para e apaga o serviço e a pasta após a resposta OK da bandeja.
    /// Devolve false se nem o PowerShell pôde ser iniciado (o marcador fica para o próximo boot).
    /// </summary>
    public static bool SpawnUninstallCleanup(ICommandRunner runner)
    {
        try
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), "SysGuard-cleanup.ps1");
            var programData = AppInfo.ProgramDataDirectory.Replace("'", "''");
            var script = $$"""
                $ErrorActionPreference = 'SilentlyContinue'
                Start-Sleep -Seconds 3
                $sc = Join-Path $env:SystemRoot 'System32\sc.exe'
                & $sc stop {{AppInfo.WatchdogServiceName}} | Out-Null
                & $sc stop {{AppInfo.ServiceName}} | Out-Null
                Start-Sleep -Seconds 2
                & $sc delete {{AppInfo.WatchdogServiceName}} | Out-Null
                & $sc delete {{AppInfo.ServiceName}} | Out-Null
                Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name '{{AppInfo.RunValueName}}' | Out-Null
                for ($i = 0; $i -lt 15; $i++) {
                    Remove-Item -LiteralPath '{{programData}}' -Recurse -Force | Out-Null
                    if (-not (Test-Path -LiteralPath '{{programData}}')) { break }
                    Start-Sleep -Seconds 1
                }
                Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force | Out-Null
                """;

            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
            var startInfo = new ProcessStartInfo
            {
                FileName = AppInfo.PowerShellExePath,
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Log.Error("Limpeza da desinstalação: Process.Start devolveu null.");
                return false;
            }

            Log.Info("Limpeza da desinstalação agendada.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao agendar a limpeza da desinstalação.", ex);
            return false;
        }
    }

    /// <summary>Chamado pelo serviço ao iniciar: se havia desinstalação pendente, conclui e pede parada.</summary>
    public static bool TryCompletePendingDelete()
    {
        if (!File.Exists(AppInfo.PendingDeletePath)) return false;

        Log.Info("Desinstalação pendente detectada; concluindo.");
        try
        {
            SystemState.CreateDefault().RemoveProtection();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao restaurar durante a desinstalação pendente.", ex);
        }

        if (!SpawnUninstallCleanup(Runner))
            Log.Error("Limpeza pendente: nova tentativa ficará para o próximo boot.");
        return true;
    }

    public static void WriteRunKey()
    {
        using var key = Registry.LocalMachine.CreateSubKey(AppInfo.RunKeyPath, writable: true);
        key?.SetValue(AppInfo.RunValueName, $"\"{AppInfo.InstalledExePath}\" --tray");
        Log.Info("Início automático registrado.");
    }

    public static void DeleteRunKey()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(AppInfo.RunKeyPath, writable: true);
            key?.DeleteValue(AppInfo.RunValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao remover o início automático.", ex);
        }
    }

    /// <summary>
    /// Para o serviço vigia e depois o principal e só devolve true quando ambos estão STOPPED. O vigia
    /// precisa parar primeiro: vivo, ele reiniciaria o principal no meio do reparo/desinstalação.
    /// </summary>
    private static bool StopExistingService() =>
        StopService(AppInfo.WatchdogServiceName) && StopService(AppInfo.ServiceName);

    private static bool StopService(string serviceName)
    {
        if (!ScService.IsInstalled(serviceName)) return true;

        int state = ScService.GetStateCode(Runner, serviceName);
        if (state == 2) // START_PENDING: deixa terminar de subir para poder parar limpo
        {
            ScService.WaitForState(Runner, running: true, TimeSpan.FromSeconds(20), serviceName);
            state = ScService.GetStateCode(Runner, serviceName);
        }

        if (state == 3) return ScService.WaitForState(Runner, running: false, TimeSpan.FromSeconds(20), serviceName);

        if (state == 4)
        {
            ScService.Stop(Runner, serviceName);
            if (!ScService.WaitForState(Runner, running: false, TimeSpan.FromSeconds(25), serviceName))
            {
                Log.Error($"Instalação: o serviço {serviceName} não parou; abortando.");
                return false;
            }

            return true;
        }

        return state is 1 or -1 or 0; // parado ou já não existe
    }

    /// <summary>Cria as pastas, recusando reparse points e limpando restos de instalação antiga.</summary>
    private static string? PrepareTrustedDirectories(bool fresh)
    {
        try
        {
            var directory = AppInfo.ProgramDataDirectory;
            if (File.Exists(directory))
                return "Existe um arquivo onde deveria ficar a pasta do app: " + directory;

            if (Directory.Exists(directory))
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if (!fresh) return "A pasta do app é um reparse point inesperado; instalação abortada.";
                    Directory.Delete(directory, recursive: false); // apaga só o link
                }
                else if (fresh)
                {
                    Directory.Delete(directory, recursive: true);
                }
            }

            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(AppInfo.StateDirectory);
            Directory.CreateDirectory(AppInfo.LogsDirectory);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao preparar a pasta de instalação.", ex);
            return "Não foi possível preparar a pasta de instalação: " + ex.Message;
        }
    }

    private static string? CopyExecutable(string sourceExePath)
    {
        var temporary = AppInfo.InstalledExePath + ".new";
        try
        {
            var source = Path.GetFullPath(sourceExePath);
            var target = Path.GetFullPath(AppInfo.InstalledExePath);
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return null;

            if (File.Exists(temporary)) File.Delete(temporary);
            File.Copy(source, temporary, overwrite: true);
            if (File.Exists(target))
            {
                var existing = File.GetAttributes(target);
                if ((existing & FileAttributes.ReadOnly) != 0) File.SetAttributes(target, existing & ~FileAttributes.ReadOnly);
            }

            try
            {
                File.Move(temporary, target, overwrite: true);
            }
            catch (IOException)
            {
                // Atualização com a bandeja antiga ainda rodando a partir do exe instalado:
                // renomear o exe em uso é permitido no Windows; substituir direto nem sempre.
                var previous = target + ".old";
                TryDeleteFile(previous);
                File.Move(target, previous);
                File.Move(temporary, target);
                TryDeleteFile(previous);
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao copiar o executável para ProgramData.", ex);
            TryDeleteFile(temporary);
            return "Não foi possível copiar o executável: " + ex.Message;
        }
    }

    /// <summary>
    /// Dono vira Administradores e ACL: só SYSTEM/Administradores no estado; Usuários podem executar
    /// o exe. Qualquer falha aborta a instalação (antes só era registrada em log).
    /// </summary>
    private static bool ApplyAcls()
    {
        bool ok = true;

        var takeown = Path.Combine(Environment.SystemDirectory, "takeown.exe");
        var owner = Runner.Run(takeown, $"/f \"{AppInfo.ProgramDataDirectory}\" /r /d Y /a", 60_000);
        if (!owner.Success)
        {
            Log.Error("takeown falhou: " + owner.Output);
            ok = false;
        }

        string[] privileged = ["*S-1-5-18:(OI)(CI)F", "*S-1-5-32-544:(OI)(CI)F"];
        ok &= GrantDirectory(AppInfo.ProgramDataDirectory, [.. privileged, "*S-1-5-32-545:(OI)(CI)RX"]);
        ok &= GrantDirectory(AppInfo.StateDirectory, privileged);
        ok &= GrantDirectory(AppInfo.LogsDirectory, privileged);

        var attrib = Path.Combine(Environment.SystemDirectory, "attrib.exe");
        var hidden = Runner.Run(attrib, $"+h \"{AppInfo.ProgramDataDirectory}\"", 10_000);
        if (!hidden.Success) Log.Info("attrib +h falhou (pasta continuará visível): " + hidden.Output);

        return ok;
    }

    private static bool GrantDirectory(string path, string[] grants)
    {
        bool ok = true;
        ok &= RunIcacls(path, "/inheritance:r");
        foreach (var grant in grants) ok &= RunIcacls(path, "/grant:r " + grant);
        return ok;
    }

    private static bool RunIcacls(string path, string arguments)
    {
        var icacls = Path.Combine(Environment.SystemDirectory, "icacls.exe");
        var result = Runner.Run(icacls, $"\"{path}\" {arguments}", 30_000);
        if (!result.Success) Log.Error($"icacls falhou em '{path}': {result.Output}");
        return result.Success;
    }

    /// <summary>Estado original completo: hosts + DNS + políticas guardados.</summary>
    private static bool HasCompleteState() =>
        File.Exists(AppInfo.HostsBackupPath)
        && File.Exists(AppInfo.DnsStatePath)
        && File.Exists(AppInfo.PoliciesStatePath);

    private static bool WaitForPipe(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        string lastResponse = "<nenhuma tentativa>";
        while (DateTimeOffset.UtcNow < deadline)
        {
            lastResponse = PipeClient.Send("PING", 2000);
            if (lastResponse == "OK") return true;
            Thread.Sleep(500);
        }

        Log.Error("Instalação: o pipe do serviço não respondeu. Última resposta: " + lastResponse);
        return false;
    }

    /// <summary>Espera o serviço responder PROTEGIDO e sem camada degradada.</summary>
    private static bool WaitForHealthy(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = PipeClient.Send("STATUS", 2500);
            if (status.StartsWith("OK PROTECTED", StringComparison.Ordinal)
                && !status.Contains("DEGRADED", StringComparison.Ordinal))
            {
                return true;
            }

            Thread.Sleep(700);
        }

        return false;
    }

    /// <summary>Depois de uma falha: se era reparo, tenta religar a instalação anterior.</summary>
    private static void Rollback(SystemState? state, bool wasInstalled)
    {
        PreserveServiceLog();
        try
        {
            if (wasInstalled && TryRestartExistingService())
            {
                Log.Info("Rollback: instalação anterior religada com sucesso.");
                return;
            }

            ScService.Stop(Runner, AppInfo.WatchdogServiceName);
            ScService.Stop(Runner);
            if (!ScService.WaitForState(Runner, running: false, TimeSpan.FromSeconds(15)))
                Log.Error("Rollback: o serviço não parou; a remoção pode falhar.");
            if (!ScService.WaitForState(Runner, running: false, TimeSpan.FromSeconds(15), AppInfo.WatchdogServiceName))
                Log.Error("Rollback: o serviço vigia não parou; a remoção pode falhar.");
            ScService.Delete(Runner, AppInfo.WatchdogServiceName);
            ScService.Delete(Runner);
            DeleteRunKey();
            (state ?? SystemState.CreateDefault()).RemoveProtection();
        }
        catch (Exception ex)
        {
            Log.Error("Rollback da instalação falhou.", ex);
        }

        TryDeleteDirectory(AppInfo.ProgramDataDirectory);
        TryDeleteFile(AppInfo.InstallLockPath);
    }

    /// <summary>Copia os logs dos serviços para %TEMP% antes de apagar a pasta (diagnóstico pós-falha).</summary>
    private static void PreserveServiceLog()
    {
        CopyLog(AppInfo.ServiceLogPath, "SysGuard-service.log");
        CopyLog(AppInfo.WatchdogLogPath, "SysGuard-watchdog.log");
    }

    private static void CopyLog(string source, string targetName)
    {
        try
        {
            if (!File.Exists(source)) return;
            var target = Path.Combine(Path.GetTempPath(), targetName);
            File.Copy(source, target, overwrite: true);
            Log.Info("Log do serviço preservado em " + target);
        }
        catch (Exception ex)
        {
            Log.Error("Não foi possível preservar o log do serviço.", ex);
        }
    }

    private static bool TryRestartExistingService()
    {
        try
        {
            if (!ScService.IsInstalled()) return false;
            ScService.Start(Runner);
            if (!ScService.WaitForState(Runner, running: true, TimeSpan.FromSeconds(20))
                || !WaitForPipe(TimeSpan.FromSeconds(15)))
            {
                return false;
            }

            if (ScService.IsInstalled(AppInfo.WatchdogServiceName))
            {
                ScService.Start(Runner, AppInfo.WatchdogServiceName);
                ScService.WaitForState(Runner, running: true, TimeSpan.FromSeconds(15), AppInfo.WatchdogServiceName);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Rollback: falha ao religar o serviço anterior.", ex);
            return false;
        }
    }

    /// <summary>
    /// Marca "instalação/reparo/remoção em andamento": os dois serviços se vigiam e não podem religar um
    /// ao outro durante uma parada intencional. O marcador é removido em todos os caminhos de saída.
    /// </summary>
    private static void WriteInstallLock()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.ProgramDataDirectory);
            File.WriteAllText(
                AppInfo.InstallLockPath,
                DateTimeOffset.Now.ToString("O") + "\n",
                new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log.Error("Instalação: não foi possível gravar o marcador de instalação.", ex);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Não foi possível apagar '{path}'.", ex);
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Não foi possível apagar '{path}'.", ex);
            return false;
        }
    }
}
