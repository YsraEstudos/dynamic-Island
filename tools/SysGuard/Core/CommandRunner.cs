using System.Diagnostics;

namespace SysGuard;

internal sealed record CommandResult(int ExitCode, string Output)
{
    public bool Success => ExitCode == 0;
}

/// <summary>Executa um utilitário do Windows (netsh, sc, icacls) sem janela.</summary>
internal interface ICommandRunner
{
    CommandResult Run(string fileName, string arguments, int timeoutMs = 30_000);
}

internal sealed class ProcessCommandRunner : ICommandRunner
{
    public CommandResult Run(string fileName, string arguments, int timeoutMs = 30_000)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Environment.SystemDirectory,
                },
            };

            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* já saiu */ }
                return new CommandResult(-1, "Tempo esgotado: " + fileName + " " + arguments);
            }

            var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            return new CommandResult(process.ExitCode, output.Trim());
        }
        catch (Exception ex)
        {
            return new CommandResult(-1, ex.Message);
        }
    }
}
