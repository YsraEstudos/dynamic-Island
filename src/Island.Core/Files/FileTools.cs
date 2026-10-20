using System.Diagnostics;

namespace Island.Core.Files;

public static class FileTools
{
    public static string? Find(string name)
    {
        string executable = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var directories = new List<string> { Path.Combine(AppContext.BaseDirectory, "tools") };
        directories.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        if (name == "soffice") directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program"));
        foreach (string directory in directories)
        {
            try { string path = Path.Combine(directory.Trim('"'), executable); if (File.Exists(path)) return path; }
            catch (ArgumentException) { }
        }
        if (name == "gswin64c")
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gs");
            if (Directory.Exists(root)) return Directory.GetFiles(root, executable, SearchOption.AllDirectories).LastOrDefault();
        }
        return null;
    }

    public static string? MissingFor(FileAction action) => action switch
    {
        FileAction.Video or FileAction.Audio
            when Find("ffmpeg") is null => "Instale o FFmpeg e adicione ao PATH, ou coloque ffmpeg.exe na pasta tools do aplicativo.",
        FileAction.OfficePdf when Find("soffice") is null => "Instale o LibreOffice para converter DOCX, PPTX e XLSX em PDF.",
        FileAction.CompressPdf when Find("gswin64c") is null => "Instale o Ghostscript (64 bits) para comprimir PDFs.",
        _ => null
    };

    public static async Task RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        Task<string> errors = process.StandardError.ReadToEndAsync(token);
        Task<string> output = process.StandardOutput.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        string error = await errors;
        await output;
        if (process.ExitCode != 0) throw new InvalidOperationException(
            $"{Path.GetFileNameWithoutExtension(executable)} não conseguiu concluir. " + error[^Math.Min(error.Length, 1800)..]);
    }
}
