using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SysGuard;

internal sealed record ServiceQuery(bool Installed, bool Running);

/// <summary>
/// Operações nos serviços do Windows via sc.exe (sem pacotes externos). O SysGuard tem dois serviços:
/// o principal (<see cref="AppInfo.ServiceName"/>) e o gêmeo (<see cref="AppInfo.WatchdogServiceName"/>),
/// que se vigiam; os métodos aceitam o nome do serviço quando não é o principal.
/// </summary>
internal static class ScService
{
    /// <summary>Instalado? A chave em HKLM é legível até por usuário comum.</summary>
    public static bool IsInstalled(string serviceName = AppInfo.ServiceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(AppInfo.ServiceRegistryPathFor(serviceName));
            return key is not null;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao consultar o registro do serviço.", ex);
            return false;
        }
    }

    public static ServiceQuery Query(ICommandRunner runner, string serviceName = AppInfo.ServiceName)
    {
        if (!IsInstalled(serviceName)) return new ServiceQuery(false, false);
        var result = runner.Run(AppInfo.ScExePath, $"query {serviceName}", 15_000);
        if (result.ExitCode == 1060) return new ServiceQuery(false, false);
        return new ServiceQuery(IsInstalled(serviceName), ParseStateCode(result.Output) == 4);
    }

    /// <summary>Cria/atualiza o serviço principal (binPath com --service).</summary>
    public static CommandResult Create(ICommandRunner runner, string installedExePath) =>
        CreateService(runner, AppInfo.ServiceName, AppInfo.ServiceDisplayName, installedExePath, "--service");

    /// <summary>Cria/atualiza o serviço gêmeo (binPath com --watchdog-service).</summary>
    public static CommandResult CreateWatchdog(ICommandRunner runner, string installedExePath) =>
        CreateService(
            runner, AppInfo.WatchdogServiceName, AppInfo.WatchdogServiceDisplayName, installedExePath, "--watchdog-service");

    private static CommandResult CreateService(
        ICommandRunner runner, string serviceName, string displayName, string installedExePath, string serviceArgument)
    {
        var binPath = Quote(installedExePath + " " + serviceArgument);
        var result = runner.Run(
            AppInfo.ScExePath,
            $"create {serviceName} binPath= {binPath} start= auto DisplayName= \"{displayName}\"");
        if (result.Success) return result;

        // 1073 = serviço já existe; nesse caso tentamos apenas atualizar o caminho e o tipo de início.
        if (result.ExitCode == 1073)
        {
            return runner.Run(
                AppInfo.ScExePath,
                $"config {serviceName} binPath= {binPath} start= auto");
        }

        return result;
    }

    public static CommandResult SetDescription(ICommandRunner runner) =>
        runner.Run(AppInfo.ScExePath, $"description {AppInfo.ServiceName} \"{AppInfo.ServiceDescription}\"");

    public static CommandResult SetWatchdogDescription(ICommandRunner runner) =>
        runner.Run(
            AppInfo.ScExePath,
            $"description {AppInfo.WatchdogServiceName} \"{AppInfo.WatchdogServiceDescription}\"");

    /// <summary>
    /// Ações de recuperação do SCM: reinicia o serviço em 5 s, 10 s e 30 s depois de uma queda, com o
    /// contador de falhas reiniciando a cada 24 h. Parada limpa (sc stop, desinstalação ou parada pela
    /// bandeja) não dispara ação de falha, então a válvula de segurança continua funcionando.
    /// </summary>
    public static CommandResult ConfigureRecovery(ICommandRunner runner, string serviceName = AppInfo.ServiceName) =>
        runner.Run(
            AppInfo.ScExePath,
            $"failure {serviceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000");

    public static CommandResult Start(ICommandRunner runner, string serviceName = AppInfo.ServiceName) =>
        runner.Run(AppInfo.ScExePath, $"start {serviceName}", 30_000);

    public static CommandResult Stop(ICommandRunner runner, string serviceName = AppInfo.ServiceName) =>
        runner.Run(AppInfo.ScExePath, $"stop {serviceName}", 30_000);

    public static CommandResult Delete(ICommandRunner runner, string serviceName = AppInfo.ServiceName) =>
        runner.Run(AppInfo.ScExePath, $"delete {serviceName}", 30_000);

    /// <summary>
    /// Espera o serviço chegar ao estado desejado. Usa o código numérico do sc (1 STOPPED,
    /// 4 RUNNING): só "não está RUNNING" não serve, porque STOP_PENDING também não é RUNNING.
    /// </summary>
    public static bool WaitForState(
        ICommandRunner runner, bool running, TimeSpan timeout, string serviceName = AppInfo.ServiceName)
    {
        int desired = running ? 4 : 1;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (GetStateCode(runner, serviceName) == desired) return true;
            Thread.Sleep(300);
        }

        return false;
    }

    public static bool IsRunning(ICommandRunner runner, string serviceName = AppInfo.ServiceName) =>
        GetStateCode(runner, serviceName) == 4;

    internal static int GetStateCode(ICommandRunner runner, string serviceName = AppInfo.ServiceName)
    {
        var result = runner.Run(AppInfo.ScExePath, $"query {serviceName}", 15_000);
        return result.ExitCode == 0 ? ParseStateCode(result.Output) : -1;
    }

    /// <summary>Extrai o estado numérico do sc ("STATE : 4  RUNNING" ou "ESTADO : 4 RUNNING").</summary>
    internal static int ParseStateCode(string output)
    {
        var match = Regex.Match(
            output,
            @"\b([1-7])\s+(?:STOPPED|START_PENDING|STOP_PENDING|RUNNING|CONTINUE_PENDING|PAUSE_PENDING|PAUSED)\b",
            RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out int code) ? code : -1;
    }

    /// <summary>
    /// Garante o ImagePath do serviço principal com o caminho entre aspas: sc pode gravar o caminho sem
    /// aspas, o que quebra o SCM se o ProgramData for redirecionado para um caminho com espaços.
    /// </summary>
    public static bool EnsureImagePath(string installedExePath, out string? error) =>
        EnsureServiceImagePath(AppInfo.ServiceName, Quote(installedExePath) + " --service", out error);

    /// <summary>Mesma garantia para o serviço gêmeo (argumento --watchdog-service).</summary>
    public static bool EnsureWatchdogImagePath(string installedExePath, out string? error) =>
        EnsureServiceImagePath(AppInfo.WatchdogServiceName, Quote(installedExePath) + " --watchdog-service", out error);

    private static bool EnsureServiceImagePath(string serviceName, string desired, out string? error)
    {
        error = null;
        try
        {
            using (var read = Registry.LocalMachine.OpenSubKey(AppInfo.ServiceRegistryPathFor(serviceName)))
            {
                if (read?.GetValue("ImagePath") is string current
                    && string.Equals(current, desired, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            using var writable = Registry.LocalMachine.OpenSubKey(
                AppInfo.ServiceRegistryPathFor(serviceName), writable: true);
            if (writable is null)
            {
                error = "Chave do serviço inacessível para ajustar o ImagePath.";
                return false;
            }

            writable.SetValue("ImagePath", desired, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Error("Falha ao ajustar o ImagePath do serviço.", ex);
            return false;
        }
    }

    /// <summary>Executa um script PowerShell escondido (usado só na limpeza final da desinstalação).</summary>
    public static void RunPowerShellHidden(ICommandRunner runner, string scriptPath)
    {
        runner.Run(
            AppInfo.PowerShellExePath,
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            20_000);
    }

    public static string Quote(string path) => "\"" + path.Replace("\"", "\\\"") + "\"";
}
