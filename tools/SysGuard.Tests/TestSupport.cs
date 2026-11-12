namespace SysGuard.Tests;

using SysGuard;

/// <summary>Executor falso: registra as chamadas e devolve o resultado configurado.</summary>
internal sealed class RecordingCommandRunner : ICommandRunner
{
    public List<(string FileName, string Arguments)> Calls { get; } = [];

    public CommandResult Result { get; set; } = new(0, string.Empty);

    public CommandResult Run(string fileName, string arguments, int timeoutMs = 30_000)
    {
        Calls.Add((fileName, arguments));
        return Result;
    }
}

internal static class TestPaths
{
    public static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SysGuard-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Deixa para o %TEMP%.
        }
    }

    public static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
}
