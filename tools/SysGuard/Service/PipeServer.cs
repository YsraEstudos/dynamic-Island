using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace SysGuard;

/// <summary>
/// Servidor do pipe de controle usando a API gerenciada do .NET (async), com DACL explícita:
/// SYSTEM e Administradores totais, usuários interativos leitura/escrita. Sem rótulo de integridade,
/// o objeto é tratado como médio pelo Windows, então a bandeja (processo comum) conecta normalmente
/// e um processo elevado (instalador) também. Nenhum cliente de rede tem o SID INTERACTIVE.
/// </summary>
internal sealed class PipeServer : IDisposable
{
    private const int ReadTimeoutMs = 10_000;
    private const int BufferSize = 4096;
    private const int MaxInstances = 4;

    private readonly string _pipeName;
    private readonly Func<string, string> _handler;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private Task? _task;
    private bool _disposed;

    public PipeServer(string pipeName, Func<string, string> handler)
    {
        _pipeName = pipeName;
        _handler = handler;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_task is not null) return;
            _task = Task.Run(LoopAsync);
            Log.Info($"Pipe: servidor de controle iniciado ('{_pipeName}').");
        }
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServer();
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, BufferSize, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), BufferSize, leaveOpen: true) { AutoFlush = true };
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(ReadTimeoutMs);

                string? request = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (request is null) continue;

                string response = SafeHandle(request);
                await writer.WriteLineAsync(response.AsMemory(), timeout.Token).ConfigureAwait(false);
                await writer.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Parada do serviço ou cliente que conectou e não enviou nada a tempo.
            }
            catch (IOException ex)
            {
                Log.Info("Pipe: conexão encerrada (" + ex.Message + ").");
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Error("Pipe: acesso negado na conexão.", ex);
            }
            catch (Exception ex)
            {
                Log.Error("Pipe: falha no servidor; nova tentativa em 500 ms.", ex);
                try { await Task.Delay(500, _stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    private NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        // S-1-5-4: INTERACTIVE (não existe em logons de rede).

        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(administrators, PipeAccessRights.FullControl, AccessControlType.Allow));
        // FullControl evita surpresas de mapeamento de direitos genéricos; o comando sensível continua
        // exigindo senha, e o SID INTERACTIVE não existe em logons de rede (pipe não é remoto).
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier("S-1-5-4"), PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            MaxInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            BufferSize,
            BufferSize,
            security);
    }

    private string SafeHandle(string request)
    {
        try
        {
            return _handler(request);
        }
        catch (Exception ex)
        {
            Log.Error("Pipe: handler lançou exceção.", ex);
            return "ERR INTERNAL";
        }
    }

    public void Dispose()
    {
        Task? task;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            task = _task;
        }

        _stop.Cancel();
        try
        {
            task?.Wait(2000);
        }
        catch (AggregateException)
        {
            // Encerramento normal: a tarefa termina em OperationCanceledException.
        }

        _stop.Dispose();
    }
}
