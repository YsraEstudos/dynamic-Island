namespace SysGuard;

/// <summary>Resultado da checagem local de senha do <c>--force-uninstall</c>.</summary>
internal sealed record ForceUninstallCheck(LocalPasswordDecision Decision, int ExitCode, string Message)
{
    public bool Allowed => Decision == LocalPasswordDecision.Allowed;
}

/// <summary>
/// O <c>--force-uninstall</c> é a recuperação do SysGuard (serve até com o serviço fora): por isso a
/// senha é verificada <b>localmente</b>, lendo o <c>config.dat</c>, em vez de pedir ao serviço pelo pipe.
/// Nada é restaurado nem apagado antes desta checagem passar.
/// <para>
/// Limite honesto: sem senha configurada (instalação incompleta/corrompida) a remoção forçada fica
/// bloqueada de propósito e a saída é a recuperação manual do README — quem é administrador consegue
/// remover a pasta de qualquer forma, mas o caminho fácil e impulsivo do exe deixa de existir.
/// </para>
/// </summary>
internal static class ForceUninstallGate
{
    /// <summary>Código de saída para senha ausente/incorreta (mesmo do --uninstall via pipe).</summary>
    public const int InvalidPasswordExitCode = 5;

    public static ForceUninstallCheck Check(string? password, string configPath)
    {
        var result = LocalPasswordCheck.Verify(password, configPath);
        return result.Decision switch
        {
            LocalPasswordDecision.Allowed => new ForceUninstallCheck(result.Decision, 0, result.Message),
            LocalPasswordDecision.MissingPassword => new ForceUninstallCheck(
                result.Decision,
                InvalidPasswordExitCode,
                "A remoção forçada exige a senha. Use: SysGuard.exe --force-uninstall --silent --password <senha em base64>."),
            LocalPasswordDecision.WrongPassword => new ForceUninstallCheck(
                result.Decision,
                InvalidPasswordExitCode,
                result.Message + " Nada foi restaurado nem removido."),
            _ => new ForceUninstallCheck(result.Decision, 1, result.Message + " Use a recuperação manual do README."),
        };
    }
}
