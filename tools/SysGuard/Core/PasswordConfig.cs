using System.Globalization;
using System.Text;

namespace SysGuard;

/// <summary>
/// Leitura e gravação de <c>config.dat</c> (hash da senha). Arquivo pequeno e linear; formatos JSON
/// ficam de fora para o app continuar seguro com trimming e sem reflection.
/// </summary>
internal static class PasswordConfig
{
    private const string Magic = "SYSGUARD-PW-1";

    public static bool Exists(string path) => File.Exists(path);

    /// <summary>Carrega o arquivo; lança <see cref="InvalidDataException"/> se estiver corrompido.</summary>
    public static PasswordRecord Load(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length < 4 || !string.Equals(lines[0].Trim(), Magic, StringComparison.Ordinal))
            throw new InvalidDataException("Arquivo de senha inválido.");

        if (!int.TryParse(lines[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations)
            || iterations < 10_000)
            throw new InvalidDataException("Número de iterações inválido no arquivo de senha.");

        var salt = lines[2].Trim();
        var hash = lines[3].Trim();
        if (salt.Length == 0 || hash.Length == 0)
            throw new InvalidDataException("Hash de senha vazio.");

        return new PasswordRecord(iterations, salt, hash);
    }

    /// <summary>Grava de forma atômica, para um desligamento no meio da escrita não corromper o arquivo.</summary>
    public static void Save(string path, PasswordRecord record)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var content = string.Join(
            "\n",
            Magic,
            record.Iterations.ToString(CultureInfo.InvariantCulture),
            record.SaltBase64,
            record.HashBase64,
            string.Empty);

        var temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    public static void Create(string path, string password) =>
        Save(path, PasswordHasher.Create(password));

    public static bool Verify(string path, string password)
    {
        if (password is null) return false;
        try
        {
            return PasswordHasher.Verify(password, Load(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Error("Falha ao ler o arquivo de senha.", ex);
            return false;
        }
    }
}
