using System.Text;

namespace SysGuard;

/// <summary>
/// Log mínimo em arquivo. Nunca lança: um problema de log não pode derrubar o serviço nem travar a UI.
/// Usado pelo serviço (ProgramData) e pela UI/instalador (%TEMP%, que não exige admin).
/// </summary>
internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static void Init(string path)
    {
        lock (Gate)
        {
            _path = path;
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            }
            catch
            {
                // Sem diretório de log o app continua funcionando, só fica sem diagnóstico.
            }
        }
    }

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        lock (Gate)
        {
            if (_path is null) return;

            try
            {
                RotateIfNeeded();
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";
                if (exception is not null)
                {
                    line += Environment.NewLine + "  " + exception.GetType().FullName + ": " + exception.Message
                        + Environment.NewLine + "  " + exception.StackTrace;
                }

                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Ignorado de propósito.
            }
        }
    }

    private static void RotateIfNeeded()
    {
        if (_path is null) return;
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < MaxBytes) return;

        try
        {
            var old = _path + ".1";
            if (File.Exists(old)) File.Delete(old);
            File.Move(_path, old);
        }
        catch
        {
            // Se não der para rotacionar, segue anexando.
        }
    }
}
