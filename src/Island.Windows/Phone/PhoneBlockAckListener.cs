using System.Net.Http.Headers;
using Island.Core.Phone;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Phone;

/// <summary>
/// Subscribes to the phone's ntfy topic and delivers the block acknowledgements the Android app publishes there.
/// The topic is derived from the FCM token (<see cref="PhoneBlockAck.TopicFor"/>), so the island needs no second
/// secret and the FCM HTTP 2xx is no longer mistaken for "the phone blocked itself".
/// <para>
/// Streaming only: one long GET (<c>/json?since=…</c>, <c>ResponseHeadersRead</c>) with one JSON document per line;
/// a dropped connection reconnects after a short delay while the listener is running. Nothing is parsed by hand beyond
/// the per-line JSON. Every failure is logged and never thrown at the app.
/// </para>
/// </summary>
public sealed class PhoneBlockAckListener : IDisposable
{
    public const string BaseUrl = "https://ntfy.sh";

    /// <summary>How long a confirmed listener waits before reconnecting after a dropped stream.</summary>
    public static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    /// <summary>Five seconds of overlap on <c>since</c>, so an ack sent just before subscribing is not missed.</summary>
    private static readonly TimeSpan SinceOverlap = TimeSpan.FromSeconds(5);

    /// <summary>Acks kept for <see cref="WaitForAckAsync"/> when the phone answers before the caller starts waiting.</summary>
    private const int RecentCapacity = 16;

    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger? _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, TaskCompletionSource<PhoneBlockAck?>> _waiters = new(StringComparer.Ordinal);
    private readonly Queue<PhoneBlockAck> _recent = new();
    private CancellationTokenSource? _streamCts;
    private Task? _streamTask;
    private string? _topic;
    private bool _disposed;

    public PhoneBlockAckListener(HttpClient http, Func<DateTimeOffset>? clock = null, ILogger? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log;
    }

    /// <summary>
    /// Client for the long-lived stream: no request timeout (the stream stays open) and a short connect timeout so a
    /// broken network is retried quickly. The caller owns the returned client; <see cref="Dispose"/> of this listener
    /// does not dispose a client it did not create.
    /// </summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Raised for every ack received (heartbeats included), on a background thread. Handler exceptions are logged.</summary>
    public event Action<PhoneBlockAck>? AckReceived;

    /// <summary>
    /// Starts streaming the ack topic for <paramref name="fcmToken"/>. Idempotent: a second call with the same token
    /// does nothing and a new token restarts the stream. A blank token stops the listener.
    /// </summary>
    public void Start(string fcmToken)
    {
        if (string.IsNullOrWhiteSpace(fcmToken))
        {
            Stop();
            return;
        }

        string topic = PhoneBlockAck.TopicFor(fcmToken);
        lock (_gate)
        {
            if (_disposed) return;
            if (_streamTask is not null && string.Equals(_topic, topic, StringComparison.Ordinal)) return;
        }

        Stop();

        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            if (_disposed)
            {
                cts.Dispose();
                return;
            }
            _topic = topic;
            _streamCts = cts;
            _streamTask = Task.Run(() => StreamAsync(topic, cts.Token));
        }
    }

    /// <summary>Stops the stream and completes any pending waiter with null. Idempotent.</summary>
    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? stream;
        List<TaskCompletionSource<PhoneBlockAck?>> waiters = [];
        lock (_gate)
        {
            cts = _streamCts;
            stream = _streamTask;
            _streamCts = null;
            _streamTask = null;
            _topic = null;
            waiters.AddRange(_waiters.Values);
            _waiters.Clear();
        }

        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        foreach (TaskCompletionSource<PhoneBlockAck?> waiter in waiters) waiter.TrySetResult(null);
        // Do not block the caller (the app's thread) on the network task; it observes the cancellation and ends by
        // itself. The token source is disposed only after that, so a registration in flight cannot hit a disposed CTS.
        if (stream is null) cts?.Dispose();
        else _ = stream.ContinueWith(_ => cts!.Dispose(), TaskScheduler.Default);
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for an ack with <paramref name="messageId"/>. Returns the ack, or null on
    /// timeout/cancellation/stop. An ack that already arrived (for example between the FCM send and this call) is returned
    /// at once. Acks for other ids never satisfy the wait.
    /// </summary>
    public async Task<PhoneBlockAck?> WaitForAckAsync(string messageId, TimeSpan timeout, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (timeout < TimeSpan.Zero) timeout = TimeSpan.Zero;

        TaskCompletionSource<PhoneBlockAck?> completion;
        lock (_gate)
        {
            PhoneBlockAck? recent = _recent.FirstOrDefault(ack => string.Equals(ack.Id, messageId, StringComparison.Ordinal));
            if (recent is not null) return recent;
            if (_disposed) return null;

            completion = new TaskCompletionSource<PhoneBlockAck?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_waiters.TryAdd(messageId, completion))
            {
                // One waiter per message id is enough: a second caller for the same block shares nothing.
                return null;
            }
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            using (linked.Token.Register(() => completion.TrySetResult(null)))
            {
                return await completion.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate) _waiters.Remove(messageId);
        }
    }

    private async Task StreamAsync(string topic, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                long since = _clock().ToUnixTimeSeconds() - (long)SinceOverlap.TotalSeconds;
                string url = $"{BaseUrl}/{Uri.EscapeDataString(topic)}/json?since={since}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));

                using HttpResponseMessage response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _log?.LogWarning("ntfy answered {Status}; retrying the ack stream", (int)response.StatusCode);
                }
                else
                {
                    await using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var reader = new StreamReader(stream);
                    while (!ct.IsCancellationRequested)
                    {
                        string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                        if (line is null) break; // server closed the stream: reconnect
                        HandleLine(line);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                _log?.LogWarning("Phone ack stream failed ({Message}); retrying", ex.Message);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Phone ack stream failed unexpectedly; retrying");
            }

            try
            {
                await Task.Delay(ReconnectDelay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void HandleLine(string line)
    {
        if (!PhoneBlockAck.TryParseNtfyLine(line, out PhoneBlockAck? ack)) return;

        TaskCompletionSource<PhoneBlockAck?>? waiter = null;
        lock (_gate)
        {
            _recent.Enqueue(ack);
            while (_recent.Count > RecentCapacity) _recent.Dequeue();
            if (_waiters.Remove(ack.Id, out TaskCompletionSource<PhoneBlockAck?>? pending)) waiter = pending;
        }

        waiter?.TrySetResult(ack);
        try
        {
            AckReceived?.Invoke(ack);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Phone ack handler threw");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }
}
