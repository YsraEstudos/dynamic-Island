using System.Text.Json;

namespace Island.Windows.Storage;

/// <summary>Shared file mechanics. Each store owns its schema and recovery/failure policy.</summary>
internal static class AtomicJsonFile
{
    public static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, options);
                stream.Flush(flushToDisk: true);
            }
            Replace(temporary, path);
        }
        finally { TryDelete(temporary); }
    }

    public static async Task WriteAsync<T>(string path, T value, JsonSerializerOptions options, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, value, options, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            Replace(temporary, path);
        }
        finally { TryDelete(temporary); }
    }

    public static string Quarantine(string path)
    {
        string recovery = path + ".bad";
        if (File.Exists(recovery)) recovery += "-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
        File.Move(path, recovery);
        return recovery;
    }

    private static void Replace(string source, string target)
    {
        if (File.Exists(target)) File.Replace(source, target, null);
        else File.Move(source, target);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
