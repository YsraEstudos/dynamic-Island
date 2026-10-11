using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Island.Core.Progress;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Progress;

/// <summary>
/// Progress from the SuperPrograma Suite over a local named pipe (docs/progress-link.md). One accept loop waits for
/// connections without polling; each connection is read on its own task, one line per message. Only the current
/// user may connect. Nothing here throws: bad lines are skipped, and failures are logged and end that connection only.
/// </summary>
public sealed class NamedPipeProgressFeed : IProgressFeed
{
    private const int MaxInstances = 4;
    private const int ReadBufferChars = 1024;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly string _pipeName;
    private readonly ILogger? _log;
    private readonly object _gate = new();
    private readonly HashSet<IDisposable> _open = new();   // pipes that are listening or connected
    private readonly CancellationTokenSource _cts = new();
    private bool _started;
    private bool _disposed;

    public NamedPipeProgressFeed(string? pipeName = null, ILogger<NamedPipeProgressFeed>? log = null)
    {
        _pipeName = pipeName ?? PipeNames.Progress;
        _log = log;
    }

    public event Action<ProgressUpdate>? Updated;

    public event Action<IReadOnlyList<string>>? Dropped;

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }

        _ = AcceptLoopAsync(_cts.Token);
    }

    /// <summary>Stops listening and closes every pipe. Does not wait for the tasks to finish.</summary>
    public void Dispose()
    {
        List<IDisposable> resources;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            resources = _open.ToList();
            _open.Clear();
        }

        _cts.Cancel();
        foreach (IDisposable resource in resources) resource.Dispose();
        Updated = null;
        Dropped = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        bool firstInstance = true;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream listener;
                try
                {
                    listener = CreateListener(firstInstance);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The first instance fails when another process already owns the pipe name: stay inert.
                    if (firstInstance)
                    {
                        _log?.LogWarning(ex, "Progress pipe {Pipe} is already in use; progress is off.", _pipeName);
                        return;
                    }

                    _log?.LogDebug(ex, "Progress pipe has no free instance; retrying.");
                    await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
                    continue;
                }

                firstInstance = false;
                if (!Track(listener))
                {
                    listener.Dispose();
                    return;
                }

                try
                {
                    await listener.WaitForConnectionAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Release(listener);
                    return;
                }
                catch (Exception ex)
                {
                    Release(listener);
                    _log?.LogDebug(ex, "Progress pipe failed while waiting for a connection; retrying.");
                    await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
                    continue;
                }

                // The next instance is created at the top of the loop, right after this connection is handed off.
                _ = ServeConnectionAsync(listener, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose is shutting the feed down.
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Progress listener stopped.");
        }
    }

    private async Task ServeConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        // Updates this connection sent that have not reached a final state. They are dropped when it closes.
        var unfinished = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, ReadBufferChars, leaveOpen: true);
            await ReadLinesAsync(reader, line => HandleLine(line, unfinished), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Dispose is shutting the feed down.
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Progress connection ended with an error.");
        }
        finally
        {
            Release(pipe);
            if (unfinished.Count > 0) RaiseDropped(unfinished.ToArray());
        }
    }

    private void HandleLine(string line, HashSet<string> unfinished)
    {
        if (!ProgressMessageParser.TryParse(line, out ProgressUpdate? update))
        {
            _log?.LogDebug("Ignored a progress line that is not valid protocol v1.");
            return;
        }

        if (update.State is ProgressState.Done or ProgressState.Error or ProgressState.Cancelled)
        {
            unfinished.Remove(update.Id);
        }
        else
        {
            unfinished.Add(update.Id);
        }

        RaiseUpdated(update);
    }

    /// <summary>
    /// Calls <paramref name="onLine"/> for each line of at most <see cref="ProgressMessageParser.MaxLineChars"/> chars.
    /// A longer line is discarded up to its newline, so memory stays bounded whatever the sender writes.
    /// </summary>
    private static async Task ReadLinesAsync(TextReader reader, Action<string> onLine, CancellationToken ct)
    {
        var line = new StringBuilder();
        var buffer = new char[ReadBufferChars];
        bool tooLong = false;

        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) break;

            ReadOnlySpan<char> rest = buffer.AsSpan(0, read);
            while (!rest.IsEmpty)
            {
                int newline = rest.IndexOf('\n');
                ReadOnlySpan<char> part = newline < 0 ? rest : rest[..newline];

                if (!tooLong)
                {
                    if (line.Length + part.Length > ProgressMessageParser.MaxLineChars)
                    {
                        tooLong = true;
                        line.Clear();
                    }
                    else
                    {
                        line.Append(part);
                    }
                }

                if (newline < 0) break;

                if (!tooLong && line.Length > 0) onLine(line.ToString());
                line.Clear();
                tooLong = false;
                rest = rest[(newline + 1)..];
            }
        }

        // A sender may close without a final newline.
        if (!tooLong && line.Length > 0) onLine(line.ToString());
    }

    private NamedPipeServerStream CreateListener(bool firstInstance)
    {
        PipeOptions options = PipeOptions.Asynchronous | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.In,
            MaxInstances,
            PipeTransmissionMode.Byte,
            options,
            0,
            0,
            CurrentUserOnly());
    }

    /// <summary>Full control for the current user's SID only, so other accounts and remote clients are refused.</summary>
    private static PipeSecurity CurrentUserOnly()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier sid = identity.User ?? throw new InvalidOperationException("Current user SID is unavailable.");

        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    /// <summary>Registers a pipe for disposal by <see cref="Dispose"/>. False when the feed is already disposed.</summary>
    private bool Track(IDisposable pipe)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            _open.Add(pipe);
            return true;
        }
    }

    private void Release(IDisposable pipe)
    {
        lock (_gate) _open.Remove(pipe);
        pipe.Dispose();
    }

    private void RaiseUpdated(ProgressUpdate update)
    {
        try
        {
            Updated?.Invoke(update);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "A progress subscriber failed.");
        }
    }

    private void RaiseDropped(string[] ids)
    {
        try
        {
            Dropped?.Invoke(ids);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "A progress subscriber failed.");
        }
    }
}
