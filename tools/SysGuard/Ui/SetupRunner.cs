using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace SysGuard;

/// <summary>Entrada do modo <c>--setup</c> (instala se necessário) e <c>--force-uninstall</c> (recuperação).</summary>
internal static class SetupRunner
{
    public static int Run(string[] args)
    {
        bool forceUninstall = HasFlag(args, "--force-uninstall");
        bool silent = HasFlag(args, "--silent");

        if (!IsAdministrator())
        {
            // Chamada de remoção forçada já inválida (sem senha) não precisa de UAC nem toca no sistema.
            if (forceUninstall && silent && !HasOption(args, "--password"))
            {
                Log.Error("Remoção forçada silenciosa recusada: falta --password.");
                return ForceUninstallGate.InvalidPasswordExitCode;
            }

            int code = RelaunchElevated(args);
            if (code == InstallFlow.Canceled)
            {
                MessageBox.Show(
                    "A instalação precisa de permissões de administrador.",
                    AppInfo.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return InstallFlow.Canceled;
            }

            return code;
        }

        try
        {
            if (silent) return forceUninstall ? RunForceUninstallSilent(args) : RunSetupSilent(args);
            return forceUninstall ? RunForceUninstall(args) : RunSetup();
        }
        catch (Exception ex)
        {
            Log.Error("Setup falhou.", ex);
            MessageBox.Show(
                "Não foi possível concluir a operação.\n\n" + ex.Message,
                AppInfo.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>
    /// Recuperação com interface: pede a senha, valida localmente (sem pipe: funciona com o serviço
    /// fora) e só então confirma a remoção.
    /// </summary>
    public static int RunForceUninstall(string[] args)
    {
        string? password = null;
        if (TryDecodeOption(args, "--password", out string? decoded, out bool present)) password = decoded;
        else if (present)
        {
            MessageBox.Show(
                "O valor de --password não é base64 válido.",
                AppInfo.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return ForceUninstallGate.InvalidPasswordExitCode;
        }

        password ??= PasswordDialog.AskPassword(
            null, AppInfo.AppName, "Digite a senha para remover a proteção (recuperação):");
        if (password is null) return InstallFlow.Canceled;

        var check = ForceUninstallGate.Check(password, AppInfo.ConfigPath);
        if (!check.Allowed)
        {
            MessageBox.Show(check.Message, AppInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return check.ExitCode;
        }

        var answer = MessageBox.Show(
            "Remover a proteção e o serviço, mesmo que o serviço não esteja respondendo?",
            AppInfo.AppName,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return InstallFlow.Canceled;

        var outcome = ServiceInstaller.ForceUninstall();
        MessageBox.Show(
            outcome.Success
                ? "Proteção removida. Reinicie o computador para concluir a limpeza."
                : "Não foi possível remover: " + outcome.Error,
            AppInfo.AppName,
            MessageBoxButtons.OK,
            outcome.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        return outcome.Success ? 0 : 1;
    }

    /// <summary>Instalação sem interface, para testes automatizados/implantações. Requer admin.</summary>
    private static int RunSetupSilent(string[] args)
    {
        bool reset = HasFlag(args, "--reset");
        bool decoded = TryDecodeOption(args, "--password", out string? password, out bool passwordPresent);
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;

        if (passwordPresent && !decoded)
        {
            Log.Error("Setup silencioso recusado: --password não é base64 válido.");
            return 1;
        }

        if (password is not null && File.Exists(AppInfo.ConfigPath))
        {
            if (!reset)
            {
                Log.Error("Setup silencioso recusado: já existe senha e --reset não foi informado.");
                return 1;
            }

            // PC-14: trocar a senha exige a senha ATUAL, mesmo com o processo elevado. O resgate de
            // senha esquecida é a recuperação manual documentada (não um flag sem autenticação).
            if (!TryDecodeOption(args, "--current-password", out string? current, out bool currentPresent))
            {
                Log.Error("Troca de senha recusada: --reset exige --current-password <senha atual em base64>"
                          + (currentPresent ? " e o valor informado não é base64 válido." : "."));
                return ForceUninstallGate.InvalidPasswordExitCode;
            }

            var check = LocalPasswordCheck.Verify(current, AppInfo.ConfigPath);
            if (!check.Allowed)
            {
                Log.Error("Troca de senha recusada: " + check.Message);
                return check.Decision is LocalPasswordDecision.MissingPassword or LocalPasswordDecision.WrongPassword
                    ? ForceUninstallGate.InvalidPasswordExitCode
                    : 1;
            }
        }

        if (password is null)
        {
            Log.Info("Setup silencioso: reparando (senha existente).");
            var repair = ServiceInstaller.Install(exe, null);
            Log.Info("Setup silencioso (reparo): " + (repair.Success ? "OK" : repair.Error));
            return repair.Success ? 0 : 1;
        }

        Log.Info("Setup silencioso: instalando com senha nova.");
        var outcome = ServiceInstaller.Install(exe, password);
        Log.Info("Setup silencioso: " + (outcome.Success ? "OK" : outcome.Error));
        return outcome.Success ? 0 : 1;
    }

    /// <summary>
    /// Remoção forçada silenciosa: exige a senha e a confere localmente contra o config.dat antes de
    /// restaurar o sistema ou agendar a limpeza. Nada destrutivo roda se a checagem falhar.
    /// </summary>
    private static int RunForceUninstallSilent(string[] args)
    {
        bool decoded = TryDecodeOption(args, "--password", out string? password, out bool present);
        if (!decoded)
        {
            string reason = present ? "o valor de --password não é base64 válido" : "falta --password <senha em base64>";
            Log.Error("Remoção forçada recusada: " + reason + ".");
            return ForceUninstallGate.InvalidPasswordExitCode;
        }

        var check = ForceUninstallGate.Check(password, AppInfo.ConfigPath);
        if (!check.Allowed)
        {
            Log.Error("Remoção forçada recusada: " + check.Message);
            return check.ExitCode;
        }

        var outcome = ServiceInstaller.ForceUninstall();
        Log.Info("Desinstalação forçada silenciosa: " + (outcome.Success ? "OK" : outcome.Error));
        return outcome.Success ? 0 : 1;
    }

    /// <summary>Lê uma opção no formato <c>--nome &lt;base64&gt;</c>; distingue ausente de valor inválido.</summary>
    internal static bool TryDecodeOption(string[] args, string option, out string? value, out bool present)
    {
        value = null;
        present = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase)) continue;
            present = true;
            if (i + 1 >= args.Length) return false;

            var encoded = args[i + 1];
            // Base64 padrão não usa '-', então um valor começando com "--" é outra opção (valor faltando).
            if (encoded.StartsWith("--", StringComparison.Ordinal)) return false;
            try
            {
                value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return false;
    }

    internal static string? DecodeOption(string[] args, string option) =>
        TryDecodeOption(args, option, out string? value, out _) ? value : null;

    internal static bool HasOption(string[] args, string option) =>
        args.Any(a => string.Equals(a, option, StringComparison.OrdinalIgnoreCase));

    private static int RunSetup()
    {
        bool installed = ScService.IsInstalled();
        bool needsPassword = !File.Exists(AppInfo.ConfigPath);
        using var form = new SetupForm(repair: installed && !needsPassword, needsPassword);
        return form.ShowDialog() == DialogResult.OK ? 0 : InstallFlow.Canceled;
    }

    private static int RelaunchElevated(string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? Application.ExecutablePath,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = string.Join(' ', args),
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return 1;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return InstallFlow.Canceled;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao elevar o instalador.", ex);
            return 1;
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    internal static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Fluxo do processo comum (não elevado) para abrir o instalador com UAC.</summary>
internal static class InstallFlow
{
    public const int Canceled = 2;

    /// <summary>Abre o setup elevado e espera. Devolve o código de saída (0 = sucesso).</summary>
    public static int RunSetupElevated(bool repair)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? Application.ExecutablePath,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = repair ? "--setup --repair" : "--setup",
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return 1;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return Canceled;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao abrir o instalador elevado.", ex);
            return 1;
        }
    }
}
