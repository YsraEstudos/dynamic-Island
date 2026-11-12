using System.IO.Pipes;
using System.Text;

namespace SysGuard;

/// <summary>
/// Cliente do pipe de controle: uma requisição por conexão, com timeout. Nunca bloqueia a UI para
/// sempre — se o serviço não responder, devolve "ERR UNAVAILABLE".
/// </summary>
internal static class PipeClient
{
    public static string Send(string request, int timeoutMs = 5000, string? pipeName = null)
    {
        NamedPipeClientStream? client = null;
        try
        {
            client = new NamedPipeClientStream(".", pipeName ?? AppInfo.PipeName, PipeDirection.InOut, PipeOptions.None);
            client.Connect(timeoutMs);

            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };
            using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);

            writer.WriteLine(request.ReplaceLineEndings(" "));

            var readTask = Task.Run(() => reader.ReadLine());
            _ = readTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            if (!readTask.Wait(timeoutMs)) return "ERR TIMEOUT";

            return readTask.Result ?? "ERR EMPTY";
        }
        catch (TimeoutException)
        {
            return "ERR UNAVAILABLE";
        }
        catch (AggregateException)
        {
            return "ERR UNAVAILABLE";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return "ERR UNAVAILABLE";
        }
        finally
        {
            try { client?.Dispose(); } catch { /* já descartado */ }
        }
    }

    /// <summary>Envia comandos com campos codificados em base64 (senhas podem conter espaços).</summary>
    public static string SendEncoded(string command, int timeoutMs, params string[] fields)
    {
        var builder = new StringBuilder(command);
        foreach (var field in fields)
        {
            builder.Append(' ').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(field)));
        }

        return Send(builder.ToString(), timeoutMs);
    }
}
