namespace SysGuard;

/// <summary>Resultado da verificação local da senha (sem pipe, funciona com o serviço fora).</summary>
internal enum LocalPasswordDecision
{
    Allowed,
    MissingPassword,
    WrongPassword,
    PasswordNotConfigured,
    PasswordUnreadable,
}

internal sealed record LocalPasswordResult(LocalPasswordDecision Decision, string Message)
{
    public bool Allowed => Decision == LocalPasswordDecision.Allowed;
}

/// <summary>
/// Verificação da senha direto no <c>config.dat</c>, para os caminhos elevados de manutenção que não
/// podem depender do pipe do serviço (<c>--force-uninstall</c> e troca de senha com <c>--reset</c>).
/// O arquivo só é legível por SYSTEM/Administradores — o processo de setup já roda elevado.
/// </summary>
internal static class LocalPasswordCheck
{
    public static LocalPasswordResult Verify(string? password, string configPath)
    {
        if (string.IsNullOrEmpty(password))
        {
            return new LocalPasswordResult(
                LocalPasswordDecision.MissingPassword,
                "A operação exige a senha atual (base64).");
        }

        if (!File.Exists(configPath))
        {
            return new LocalPasswordResult(
                LocalPasswordDecision.PasswordNotConfigured,
                "Não há senha configurada nesta instalação; use a recuperação manual documentada no README.");
        }

        PasswordRecord record;
        try
        {
            record = PasswordConfig.Load(configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new LocalPasswordResult(
                LocalPasswordDecision.PasswordUnreadable,
                "Não foi possível ler o arquivo de senha: " + ex.Message);
        }

        return PasswordHasher.Verify(password, record)
            ? new LocalPasswordResult(LocalPasswordDecision.Allowed, "Senha confirmada.")
            : new LocalPasswordResult(LocalPasswordDecision.WrongPassword, "Senha incorreta; nada foi alterado.");
    }
}
