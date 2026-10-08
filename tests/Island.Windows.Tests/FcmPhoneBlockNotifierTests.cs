using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Island.Windows.Phone;

namespace Island.Windows.Tests;

public sealed class FcmPhoneBlockNotifierTests : IDisposable
{
    private const string TokenUri = "https://oauth2.googleapis.com/token";

    private sealed record Seen(string Url, string Body, string? Authorization);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "island-fcm-" + Guid.NewGuid().ToString("N"));
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly List<Seen> _seen = [];
    private readonly List<TimeSpan> _delays = [];
    private readonly Queue<HttpStatusCode> _fcmReplies = new();
    private DateTimeOffset _now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private bool _advanceClockOnDelay;

    public FcmPhoneBlockNotifierTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        _rsa.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string WriteKey(string tokenUri = TokenUri, bool complete = true)
    {
        string path = Path.Combine(_dir, "key.json");
        var json = new Dictionary<string, string>
        {
            ["client_email"] = "island@demo.iam.gserviceaccount.com",
            ["private_key"] = _rsa.ExportPkcs8PrivateKeyPem(),
            ["token_uri"] = tokenUri,
        };
        if (complete) json["project_id"] = "demo-project";
        File.WriteAllText(path, JsonSerializer.Serialize(json));
        return path;
    }

    private FcmPhoneBlockNotifier Notifier(string? keyPath = null)
    {
        var handler = new FakeHandler(request =>
        {
            string body = request.Content!.ReadAsStringAsync().Result;
            _seen.Add(new Seen(request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
            if (request.RequestUri.ToString() == TokenUri)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"tok\",\"expires_in\":3600}") };
            HttpStatusCode status = _fcmReplies.Count > 0 ? _fcmReplies.Dequeue() : HttpStatusCode.OK;
            return new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.NotFound ? "UNREGISTERED" : "{}") };
        });
        return new FcmPhoneBlockNotifier(new HttpClient(handler), keyPath ?? WriteKey(), () => _now, (span, _) =>
        {
            _delays.Add(span);
            if (_advanceClockOnDelay) _now += span;
            return Task.CompletedTask;
        });
    }

    private IEnumerable<Seen> Sends => _seen.Where(s => s.Url.Contains("messages:send"));

    [Fact]
    public async Task Sends_one_high_priority_data_message_with_the_remaining_time()
    {
        var outcome = await Notifier().NotifyAsync(TimeSpan.FromMinutes(25), "phone-token");

        Assert.True(outcome.Delivered);
        Assert.Equal(2, _seen.Count); // OAuth token + one send
        var send = Assert.Single(Sends);
        Assert.Equal("https://fcm.googleapis.com/v1/projects/demo-project/messages:send", send.Url);
        Assert.Equal("Bearer tok", send.Authorization);

        using var doc = JsonDocument.Parse(send.Body);
        var message = doc.RootElement.GetProperty("message");
        Assert.Equal("phone-token", message.GetProperty("token").GetString());
        Assert.False(message.TryGetProperty("notification", out _)); // data only: nothing shown on the phone
        Assert.Equal("HIGH", message.GetProperty("android").GetProperty("priority").GetString());
        Assert.Equal("1500s", message.GetProperty("android").GetProperty("ttl").GetString());
        var data = message.GetProperty("data");
        Assert.Equal((25 * 60_000).ToString(), data.GetProperty("durationMs").GetString());
        Assert.Equal(_now.AddMinutes(25).ToUnixTimeMilliseconds().ToString(), data.GetProperty("endMs").GetString());
        Assert.Equal(32, data.GetProperty("id").GetString()!.Length);
    }

    [Fact]
    public async Task The_oauth_assertion_is_a_valid_rs256_jwt_for_the_service_account()
    {
        await Notifier().NotifyAsync(TimeSpan.FromMinutes(5), "t");

        string form = _seen[0].Body;
        string assertion = Uri.UnescapeDataString(form.Split("assertion=")[1]);
        string[] parts = assertion.Split('.');
        Assert.Equal(3, parts.Length);

        byte[] signature = Convert.FromBase64String(PadBase64(parts[2]));
        Assert.True(_rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), signature,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        using var claims = JsonDocument.Parse(Convert.FromBase64String(PadBase64(parts[1])));
        Assert.Equal("island@demo.iam.gserviceaccount.com", claims.RootElement.GetProperty("iss").GetString());
        Assert.Equal("https://www.googleapis.com/auth/firebase.messaging", claims.RootElement.GetProperty("scope").GetString());
        Assert.Equal(TokenUri, claims.RootElement.GetProperty("aud").GetString());
    }

    [Fact]
    public async Task The_access_token_is_reused_by_the_next_session()
    {
        var notifier = Notifier();
        await notifier.NotifyAsync(TimeSpan.FromMinutes(25), "t");
        _now += TimeSpan.FromMinutes(30);
        await notifier.NotifyAsync(TimeSpan.FromMinutes(25), "t");

        Assert.Equal(3, _seen.Count); // 1 OAuth + 2 sends
    }

    [Fact]
    public async Task An_expired_access_token_is_fetched_again()
    {
        var notifier = Notifier();
        await notifier.NotifyAsync(TimeSpan.FromMinutes(25), "t");
        _now += TimeSpan.FromMinutes(56);
        await notifier.NotifyAsync(TimeSpan.FromMinutes(25), "t");

        Assert.Equal(4, _seen.Count);
    }

    [Fact]
    public async Task A_server_error_is_retried_with_the_same_message_id()
    {
        _fcmReplies.Enqueue(HttpStatusCode.ServiceUnavailable);
        var outcome = await Notifier().NotifyAsync(TimeSpan.FromMinutes(25), "t");

        Assert.True(outcome.Delivered);
        Assert.Equal([TimeSpan.FromSeconds(5)], _delays);
        var ids = Sends.Select(s => JsonDocument.Parse(s.Body).RootElement.GetProperty("message").GetProperty("data").GetProperty("id").GetString()).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(ids[0], ids[1]);
    }

    [Fact]
    public async Task An_unauthorized_reply_drops_the_cached_token_and_retries()
    {
        _fcmReplies.Enqueue(HttpStatusCode.Unauthorized);
        var outcome = await Notifier().NotifyAsync(TimeSpan.FromMinutes(25), "t");

        Assert.True(outcome.Delivered);
        Assert.Equal(2, _seen.Count(s => s.Url == TokenUri));
    }

    [Fact]
    public async Task An_unregistered_token_is_not_retried()
    {
        _fcmReplies.Enqueue(HttpStatusCode.NotFound);
        var outcome = await Notifier().NotifyAsync(TimeSpan.FromMinutes(25), "t");

        Assert.False(outcome.Delivered);
        Assert.Contains("expirou", outcome.Detail);
        Assert.Single(Sends);
        Assert.Empty(_delays);
    }

    [Fact]
    public async Task Retries_stop_when_the_session_has_already_ended()
    {
        _advanceClockOnDelay = true;
        for (int i = 0; i < 10; i++) _fcmReplies.Enqueue(HttpStatusCode.ServiceUnavailable);
        var outcome = await Notifier().NotifyAsync(TimeSpan.FromSeconds(10), "t");

        Assert.False(outcome.Delivered);
        Assert.Equal(2, Sends.Count()); // t+0 and t+5; at t+20 nothing is left to block
    }

    [Fact]
    public async Task Gives_up_after_the_last_retry()
    {
        for (int i = 0; i < 10; i++) _fcmReplies.Enqueue(HttpStatusCode.ServiceUnavailable);
        var outcome = await Notifier().NotifyAsync(TimeSpan.FromMinutes(25), "t");

        Assert.False(outcome.Delivered);
        Assert.Equal(4, Sends.Count());
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)], _delays);
    }

    [Fact]
    public async Task Duration_is_capped_at_two_hours()
    {
        await Notifier().NotifyAsync(TimeSpan.FromHours(8), "t");

        using var doc = JsonDocument.Parse(Sends.Single().Body);
        Assert.Equal("7200000", doc.RootElement.GetProperty("message").GetProperty("data").GetProperty("durationMs").GetString());
    }

    [Fact]
    public async Task Nothing_goes_out_without_a_token_a_duration_or_a_key()
    {
        Assert.False((await Notifier().NotifyAsync(TimeSpan.FromMinutes(5), "  ")).Delivered);
        Assert.False((await Notifier().NotifyAsync(TimeSpan.Zero, "t")).Delivered);
        Assert.False((await Notifier(Path.Combine(_dir, "missing.json")).NotifyAsync(TimeSpan.FromMinutes(5), "t")).Delivered);
        Assert.Empty(_seen);
    }

    [Fact]
    public async Task An_incomplete_key_file_is_refused()
    {
        var outcome = await Notifier(WriteKey(complete: false)).NotifyAsync(TimeSpan.FromMinutes(5), "t");

        Assert.False(outcome.Delivered);
        Assert.Empty(_seen);
    }

    [Fact]
    public async Task The_signed_assertion_is_never_sent_to_a_non_google_host()
    {
        var outcome = await Notifier(WriteKey(tokenUri: "https://evil.example.com/token")).NotifyAsync(TimeSpan.FromMinutes(5), "t");

        Assert.False(outcome.Delivered);
        Assert.Empty(_seen);
    }

    private static string PadBase64(string url)
    {
        string s = url.Replace('-', '+').Replace('_', '/');
        return s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}
