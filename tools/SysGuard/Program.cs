using System.Windows.Forms;

namespace SysGuard;

internal static class Program
{
    private const string TrayMutexName = @"Local\SysGuard.Tray";

    [STAThread]
    private static int Main(string[] args)
    {
        bool isService = HasFlag(args, "--service");
        bool isWatchdog = HasFlag(args, "--watchdog-service");

        try
        {
            // Cada processo grava no seu log para os dois serviços não disputarem o mesmo arquivo.
            Log.Init(isWatchdog ? AppInfo.WatchdogLogPath
                : isService ? AppInfo.ServiceLogPath
                : Path.Combine(Path.GetTempPath(), "SysGuard.log"));

            if (isWatchdog) return WatchdogHost.Run();
            if (isService) return ServiceHost.Run();

            ApplicationConfiguration.Initialize();

            if (HasFlag(args, "--selftest")) return SelfTest.Run();
            if (HasFlag(args, "--pipetest")) return RunPipeTest(silent: HasFlag(args, "--silent"));
            if (HasFlag(args, "--setup") || HasFlag(args, "--force-uninstall")) return SetupRunner.Run(args);
            if (HasFlag(args, "--status")) return RunStatus(silent: HasFlag(args, "--silent"));
            if (HasFlag(args, "--uninstall")) return RunUninstall(args, silent: HasFlag(args, "--silent"));

            return RunTray(skipInstallCheck: HasFlag(args, "--tray-standalone"));
        }
        catch (Exception ex)
        {
            Log.Error("Falha fatal.", ex);
            if (!isService && !isWatchdog)
            {
                try
                {
                    MessageBox.Show(
                        "Ocorreu um erro inesperado.\n\n" + ex.Message,
                        AppInfo.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                catch
                {
                    // Sem interface disponível; o código de saída já indica a falha.
                }
            }

            return 1;
        }
    }

    private static int RunTray(bool skipInstallCheck)
    {
        // Instalação/atualização acontece antes do mutex da bandeja: assim, rodar um exe novo com a
        // versão antiga já em execução ainda oferece a atualização (o mutex só evita duas bandejas).
        if (!skipInstallCheck)
        {
            if (!ScService.IsInstalled())
            {
                int code = InstallFlow.RunSetupElevated(repair: false);
                if (code != 0)
                {
                    if (code == InstallFlow.Canceled)
                        MessageBox.Show(
                            "A proteção não foi ativada.",
                            AppInfo.AppName,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    return 0;
                }
            }
            else if (!ScService.IsInstalled(AppInfo.WatchdogServiceName))
            {
                // Instalação antiga (ou gêmeo removido): repara para instalar o vigia que reinicia o serviço.
                var installWatchdog = MessageBox.Show(
                    "O serviço de proteção está sem o vigia que o reinicia sozinho. Reparar a instalação com esta versão? A senha é mantida.",
                    AppInfo.AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (installWatchdog == DialogResult.Yes)
                {
                    int code = InstallFlow.RunSetupElevated(repair: true);
                    if (code != 0)
                    {
                        if (code != InstallFlow.Canceled)
                            MessageBox.Show(
                                "Não foi possível reparar a instalação.",
                                AppInfo.AppName,
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        return 0;
                    }
                }
            }
            else if (!WaitForService(TimeSpan.FromSeconds(20)))
            {
                var repair = MessageBox.Show(
                    "O serviço de proteção não está respondendo. Reparar a instalação com esta versão? A senha é mantida.",
                    AppInfo.AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (repair == DialogResult.Yes)
                {
                    int code = InstallFlow.RunSetupElevated(repair: true);
                    if (code != 0)
                    {
                        if (code != InstallFlow.Canceled)
                            MessageBox.Show(
                                "Não foi possível reparar a instalação.",
                                AppInfo.AppName,
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        return 0;
                    }
                }
            }
            else if (BinaryDiffersFromInstalled())
            {
                var update = MessageBox.Show(
                    "Já existe uma instalação do SysGuard neste computador. Atualizar/reparar com esta versão? A senha é mantida.",
                    AppInfo.AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (update == DialogResult.Yes)
                {
                    int code = InstallFlow.RunSetupElevated(repair: true);
                    if (code != 0)
                    {
                        if (code != InstallFlow.Canceled)
                            MessageBox.Show(
                                "Não foi possível atualizar a instalação.",
                                AppInfo.AppName,
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        return 0;
                    }

                    MessageBox.Show(
                        "Atualização concluída; a senha foi mantida. Reinicie o computador para concluir a troca.",
                        AppInfo.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return 0;
                }
            }
        }

        using var mutex = new Mutex(initiallyOwned: true, TrayMutexName, out bool created);
        if (!created) return 0;

        using var application = new TrayApplication();
        Application.Run(application);
        return 0;
    }

    /// <summary>True quando este exe (rodando de outro caminho) é diferente do exe instalado.</summary>
    private static bool BinaryDiffersFromInstalled()
    {
        try
        {
            var current = Environment.ProcessPath;
            var installed = AppInfo.InstalledExePath;
            if (string.IsNullOrEmpty(current) || !File.Exists(installed)) return false;
            if (string.Equals(Path.GetFullPath(current), Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase))
                return false;

            return !SameContent(current, installed);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao comparar o exe atual com o instalado.", ex);
            return false;
        }
    }

    private static bool SameContent(string first, string second)
    {
        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        if (firstInfo.Length != secondInfo.Length) return false;

        using var firstStream = File.OpenRead(first);
        using var secondStream = File.OpenRead(second);
        var firstHash = System.Security.Cryptography.SHA256.HashData(firstStream);
        var secondHash = System.Security.Cryptography.SHA256.HashData(secondStream);
        return firstHash.AsSpan().SequenceEqual(secondHash);
    }

    /// <summary>
    /// Status para automação: 0 protegido e saudável, 5 protegido degradado, 3 desprotegido, 4 indisponível.
    /// O sufixo "VPN" (VPN/túnel ativo) não muda o código: as camadas foram aplicadas, mas o DNS dela
    /// pode escapar do filtro — fica no log e no aviso da bandeja (PC-17).
    /// </summary>
    private static int RunStatus(bool silent)
    {
        string response = PipeClient.Send("STATUS", 3000);
        bool vpn = response.Contains(" VPN", StringComparison.Ordinal);
        int code = response.StartsWith("OK PROTECTED DEGRADED", StringComparison.Ordinal) ? 5
            : response.StartsWith("OK PROTECTED", StringComparison.Ordinal) ? 0
            : response.StartsWith("OK UNPROTECTED", StringComparison.Ordinal) ? 3
            : 4;
        Log.Info("Status: " + response + " (código " + code + ").");
        if (vpn)
            Log.Info("Atenção: VPN/túnel ativo detectado; o filtro de DNS pode não valer para o tráfego dela.");
        if (!silent && (code != 0 || vpn))
        {
            string message = "Status: " + response;
            if (vpn) message += "\n\nHá VPN/túnel ativo neste computador: o bloqueio de rede pode não valer para o tráfego dela.";
            MessageBox.Show(message, AppInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        return code;
    }

    /// <summary>Desinstalação via pipe (senha necessária), para automação/recuperação: 0 OK, 5 senha errada, 6 bloqueio, 7 indisponível.</summary>
    private static int RunUninstall(string[] args, bool silent)
    {
        string? password = SetupRunner.DecodeOption(args, "--password");
        if (password is null) return 5;

        string response = PipeClient.SendEncoded("UNINSTALL", 8000, password);
        int code = response == "OK" ? 0
            : response.StartsWith("ERR AUTH", StringComparison.Ordinal) ? 5
            : response.StartsWith("ERR LOCKOUT", StringComparison.Ordinal) ? 6
            : 7;
        Log.Info("Desinstalação: " + response + " (código " + code + ").");
        if (!silent)
        {
            MessageBox.Show(
                response == "OK" ? "Proteção removida." : "Não foi possível desinstalar: " + response,
                AppInfo.AppName,
                MessageBoxButtons.OK,
                response == "OK" ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }

        return code;
    }

    /// <summary>Diagnóstico sem instalar nada: valida o canal de controle (servidor + cliente).</summary>
    private static int RunPipeTest(bool silent)
    {
        string pipeName = "SysGuard.Probe." + Environment.ProcessId;
        using var server = new PipeServer(pipeName, request => request == "PING" ? "OK" : "ERR");
        server.Start();

        string response = string.Empty;
        for (int attempt = 0; attempt < 30 && response != "OK"; attempt++)
        {
            response = PipeClient.Send("PING", 1000, pipeName);
            if (response != "OK") Thread.Sleep(100);
        }

        bool ok = response == "OK";
        Log.Info(ok ? "Pipe test: OK." : "Pipe test: FALHOU; última resposta: " + response);
        if (!silent)
        {
            MessageBox.Show(
                ok ? "Canal de controle funcionando." : "Canal de controle falhou: " + response,
                AppInfo.AppName,
                MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }

        return ok ? 0 : 1;
    }

    /// <summary>Espera o serviço responder ao PING (no logon ele pode estar começando).</summary>
    private static bool WaitForService(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (PipeClient.Send("PING", 2000) == "OK") return true;
            Thread.Sleep(1000);
        }

        return false;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));
}
