using System.Net;
using System.Text;
using Island.Core.Phone;
using Island.Windows.Phone;

namespace Island.Windows.Tests;

public sealed class PhoneBlockAckListenerTests
{
    private const string NtfyMessage =
        """{"id":"ntfy-1","time":1762881282,"event":"message","topic":"di-ack-x","message":"{\"v\":1,\"id\":\"block-1\",\"applied\":true,\"a11y\":true,\"admin\":true,\"level\":\"full\",\"endMs\":1762882692123}"}""";

    private const string OtherBlockMessage =
        """{"id":"ntfy-2","time":1762881283,"event":"message","topic":"di-ack-x","message":"{\"v\":1,\"id\":\"old-block\",\"applied\":true,\"a11y\":true}"}""";

    private sealed class StreamHandler(string body) : HttpMessageHandler
    {
        public string? RequestedUrl { get; private set; }
        public string? Accept { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUrl = request.RequestUri?.ToString();
            Accept = request.Headers.Accept.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body))),
            });
        }
    }

    private static PhoneBlockAckListener Listener(StreamHandler handler, out HttpClient http)
    {
        http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return new PhoneBlockAckListener(http);
    }

    [Fact]
    public async Task The_stream_subscribes_to_the_derived_topic_and_delivers_acks()
    {
        var handler = new StreamHandler("""{"event":"open"}""" + "\n" + """{"event":"keepalive"}""" + "\n"
            + NtfyMessage + "\n" + OtherBlockMessage + "\n");
        using PhoneBlockAckListener listener = Listener(handler, out HttpClient http);
        using var _ = http;
        var received = new List<PhoneBlockAck>();
        listener.AckReceived += received.Add;

        listener.Start("token-de-teste");
        PhoneBlockAck? ack = await listener.WaitForAckAsync("block-1", TimeSpan.FromSeconds(5));

        Assert.NotNull(ack);
        Assert.True(ack.IsConfirmed);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1762882692123), ack.End);
        Assert.Contains("/di-ack-1fac79b3b4ef8b220eb2588757d9fe2e/json?since=", handler.RequestedUrl!);
        Assert.Equal(2, received.Count); // every parsed ack is raised; the tracker filters by message id
        Assert.Contains(received, a => a.Id == "block-1");
    }

    [Fact]
    public async Task An_ack_that_arrives_before_the_wait_is_not_lost()
    {
        var handler = new StreamHandler(NtfyMessage + "\n");
        using PhoneBlockAckListener listener = Listener(handler, out HttpClient http);
        using var _ = http;
        listener.Start("token-de-teste");

        // Give the stream a moment to read the line, then wait for an ack it already saw.
        await Task.Delay(300);
        PhoneBlockAck? ack = await listener.WaitForAckAsync("block-1", TimeSpan.FromMilliseconds(100));

        Assert.NotNull(ack);
        Assert.Equal("block-1", ack.Id);
    }

    [Fact]
    public async Task An_ack_for_another_block_does_not_satisfy_the_wait()
    {
        var handler = new StreamHandler(OtherBlockMessage + "\n");
        using PhoneBlockAckListener listener = Listener(handler, out HttpClient http);
        using var _ = http;
        listener.Start("token-de-teste");

        Assert.Null(await listener.WaitForAckAsync("block-1", TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task The_wait_times_out_when_the_phone_never_answers()
    {
        var handler = new StreamHandler("""{"event":"open"}""" + "\n");
        using PhoneBlockAckListener listener = Listener(handler, out HttpClient http);
        using var _ = http;
        listener.Start("token-de-teste");

        Assert.Null(await listener.WaitForAckAsync("block-1", TimeSpan.FromMilliseconds(150)));
        Assert.Null(await listener.WaitForAckAsync("block-1", TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public async Task Stop_completes_a_pending_wait_with_null()
    {
        var handler = new StreamHandler("""{"event":"open"}""" + "\n");
        var listener = Listener(handler, out HttpClient http);
        listener.Start("token-de-teste");

        Task<PhoneBlockAck?> wait = listener.WaitForAckAsync("block-1", TimeSpan.FromSeconds(30));
        listener.Stop();

        Assert.Null(await wait);
        listener.Dispose();
        http.Dispose();
    }

    [Fact]
    public async Task A_guardian_heartbeat_is_delivered_without_a_message_id()
    {
        const string guardian = """
            {"event":"message","message":"{\"v\":1,\"reason\":\"guardian-revive\",\"applied\":true,\"a11y\":true}"}
            """;
        var handler = new StreamHandler(guardian + "\n");
        using PhoneBlockAckListener listener = Listener(handler, out HttpClient http);
        using var _ = http;
        var received = new List<PhoneBlockAck>();
        listener.AckReceived += received.Add;

        listener.Start("token-de-teste");
        await Task.Delay(300);

        PhoneBlockAck ack = Assert.Single(received);
        Assert.True(ack.IsGuardianStatus);
        Assert.True(ack.IsConfirmed);
        Assert.Equal(string.Empty, ack.Id);
    }

    [Fact]
    public async Task A_blank_token_does_not_start_a_stream_and_an_empty_payload_is_ignored()
    {
        var handler = new StreamHandler("""
            {"event":"message","message":""}
            {"event":"message","message":"{\"id\":\"x\",\"applied\":true}"}
            """);
        using PhoneBlockAckListener listener = Listener(handler, out HttpClient http);
        using var _ = http;
        var received = new List<PhoneBlockAck>();
        listener.AckReceived += received.Add;

        listener.Start("   ");
        await Task.Delay(100);
        Assert.Null(handler.RequestedUrl);

        listener.Start("token-de-teste");
        Assert.Null(await listener.WaitForAckAsync("block-1", TimeSpan.FromMilliseconds(150)));
        // The ack missing a11y is delivered as "not confirmed" instead of confirming the block by omission.
        Assert.Single(received);
        Assert.False(received[0].IsConfirmed);
    }

    [Fact]
    public void The_topic_is_the_same_the_android_app_publishes_to()
    {
        // The listener and the tracker share one derivation: a mismatch would silently break every confirmation.
        Assert.Equal(PhoneBlockAck.TopicFor("token-de-teste"), PhoneBlockAck.TopicFor(" token-de-teste "));
        Assert.StartsWith("di-ack-", PhoneBlockAck.TopicFor("qualquer-token"));
    }
}
