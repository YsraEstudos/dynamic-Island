using System.Security.Cryptography;
using System.Text;

namespace SysGuard;

/// <summary>Material da senha: PBKDF2-SHA256 com sal aleatório. A senha em si nunca é gravada.</summary>
internal sealed record PasswordRecord(int Iterations, string SaltBase64, string HashBase64);

/// <summary>Hash e verificação da senha. Funções puras, sem I/O, testáveis.</summary>
internal static class PasswordHasher
{
    public const int DefaultIterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static PasswordRecord Create(string password, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (iterations < 10_000) throw new ArgumentOutOfRangeException(nameof(iterations));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, iterations);
        return new PasswordRecord(iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string password, PasswordRecord record)
    {
        if (password is null || record is null) return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(record.SaltBase64);
            expected = Convert.FromBase64String(record.HashBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0) return false;

        var actual = Derive(password, salt, record.Iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Regras da senha: pelo menos 6 caracteres, no máximo 128, sem quebras de linha (o protocolo do
    /// pipe é orientado a linhas). Espaços no meio e nas pontas são aceitos como digitados.
    /// </summary>
    public static bool IsAcceptable(string? password, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(password))
        {
            error = "Digite uma senha.";
            return false;
        }

        if (password.Length < 6)
        {
            error = "A senha precisa ter pelo menos 6 caracteres.";
            return false;
        }

        if (password.Length > 128)
        {
            error = "A senha pode ter no máximo 128 caracteres.";
            return false;
        }

        if (password.Contains('\r') || password.Contains('\n'))
        {
            error = "A senha não pode conter quebras de linha.";
            return false;
        }

        return true;
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashSize);
}
