namespace SysGuard;

/// <summary>
/// Vigia mútua dos dois serviços do SysGuard. O serviço gêmeo (<see cref="AppInfo.WatchdogServiceName"/>)
/// reinicia o principal a cada poucos segundos se ele parar; o principal faz o mesmo com o gêmeo. Isso
/// transforma o "parar serviço com UAC" (red-team PC-13) em atrito: parar um só não desliga a proteção
/// por mais que poucos segundos. Parar os dois ainda vence — o administrador da máquina sempre vence
/// (take-ownership/Safe Mode), como está documentado no README.
/// </summary>
internal static class ServiceWatchdog
{
    /// <summary>Intervalo do laço do serviço gêmeo (o principal usa um pouco mais espaçado).</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>Marcador de procedimento em andamento com mais de 10 min é resto de processo morto.</summary>
    public static readonly TimeSpan MarkerMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Garante que <paramref name="serviceName"/> está RUNNING, iniciando-o quando está parado. Ignora
    /// serviço inexistente (desinstalação em andamento) e um procedimento de instalação/desinstalação
    /// em andamento (não briga com a limpeza). Nunca lança.
    /// </summary>
    public static bool EnsureRunning(ICommandRunner runner, string serviceName)
    {
        try
        {
            // A limpeza da desinstalação e a instalação/reparo param os serviços de propósito: o vigia
            // não pode ressuscitá-los no meio do procedimento.
            if (IsProcedureInProgress(AppInfo.PendingDeletePath) || IsProcedureInProgress(AppInfo.InstallLockPath))
                return true;

            int state = ScService.GetStateCode(runner, serviceName);
            if (state == 4) return true;                // RUNNING
            if (state is 2 or 3) return true;           // START/STOP_PENDING: o próximo ciclo confere
            if (state is not 1) return false;           // 1 STOPPED; -1 = serviço não existe (não insiste)

            Log.Info("Vigia: " + serviceName + " está parado; iniciando de novo.");
            var start = ScService.Start(runner, serviceName);
            if (!start.Success)
                Log.Error("Vigia: falha ao iniciar " + serviceName + ": " + start.Output);
            return start.Success;
        }
        catch (Exception ex)
        {
            Log.Error("Vigia: falha ao verificar " + serviceName + ".", ex);
            return false;
        }
    }

    /// <summary>
    /// True quando o marcador existe e é recente; um marcador antigo (processo que morreu no meio) é
    /// removido para não deixar os serviços sem supervisão para sempre.
    /// </summary>
    internal static bool IsProcedureInProgress(string markerPath)
    {
        try
        {
            var info = new FileInfo(markerPath);
            if (!info.Exists) return false;

            if (DateTimeOffset.UtcNow - info.LastWriteTimeUtc <= MarkerMaxAge) return true;

            Log.Info("Vigia: marcador antigo ignorado e removido: " + markerPath);
            try { File.Delete(markerPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Laço de supervisão até o evento de parada. Usado pelo serviço gêmeo (main) e pelo principal
    /// (watchdog); o processo que chama decide o intervalo.
    /// </summary>
    public static void RunLoop(string serviceName, TimeSpan interval, WaitHandle stop, ICommandRunner runner)
    {
        while (!stop.WaitOne(interval))
        {
            EnsureRunning(runner, serviceName);
        }
    }
}
