using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Island.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Phone;

/// <summary>
/// Sends the phone one FCM data message ("block yourself until X") through the HTTP v1 API. Nothing runs between
/// calls: no timer, no open connection (the pooled one is dropped after 30 s idle), and the OAuth access token is
/// kept in memory for under an hour. The service-account key is read from disk per call and never logged.
/// A failed delivery is retried with the same message id (the phone ignores duplicates) while time remains.
/// </summary>
public sealed class FcmPhoneBlockNotifier : IPhoneBlockNotifier
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(120);

    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
    private const string DefaultTokenUri = "https://oauth2.googleapis.com/token";
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)];

    private readonly HttpClient _http;
    private readonly string _keyPath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILogger? _log;

    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiry;

    public FcmPhoneBlockNotifier(
        HttpClient http,
        string? keyPath = null,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ILogger? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _keyPath = keyPath ?? DefaultKeyPath;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((span, ct) => Task.Delay(span, ct));
        _log = log;
    }

    /// <summary>%LocalAppData%\DynamicIsland\fcm-service-account.json</summary>
    public static string DefaultKeyPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "fcm-service-account.json");

    /// <summary>The single client for this notifier: short timeouts, and idle connections are closed after 30 s.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(10),
    })
    { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<PhoneBlockOutcome> NotifyAsync(TimeSpan duration, string fcmToken, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(duration, fcmToken, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new PhoneBlockOutcome(false, "Envio cancelado");
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "Phone block failed unexpectedly");
            return new PhoneBlockOutcome(false, "Falha ao avisar o celular");
        }
    }

    private async Task<PhoneBlockOutcome> SendAsync(TimeSpan duration, string fcmToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fcmToken)) return new PhoneBlockOutcome(false, "Cole o token do celular nas Settings");
        if (duration <= TimeSpan.Zero) return new PhoneBlockOutcome(false, "Sem tempo restante para bloquear");
        if (duration > MaxDuration) duration = MaxDuration;

        var account = ServiceAccount.Load(_keyPath, out string? loadError);
        if (account is null) return new PhoneBlockOutcome(false, loadError!);

        string id = Guid.NewGuid().ToString("N");
        DateTimeOffset end = _clock() + duration;

        for (int attempt = 0; ; attempt++)
        {
            TimeSpan remaining = end - _clock();
            if (remaining <= TimeSpan.Zero) return new PhoneBlockOutcome(false, "O tempo acabou antes de entregar ao celular");

            Attempt result = await TrySendAsync(account, fcmToken.Trim(), id, end, remaining, ct).ConfigureAwait(false);
            if (result.Kind == AttemptKind.Delivered)
                return new PhoneBlockOutcome(true, $"Celular bloqueado até {end.ToLocalTime():HH:mm}");
            if (result.Kind == AttemptKind.Permanent || attempt >= RetryDelays.Length)
                return new PhoneBlockOutcome(false, result.Detail);

            await _delay(RetryDelays[attempt], ct).ConfigureAwait(false);
        }
    }

    private async Task<Attempt> TrySendAsync(
        ServiceAccount account, string fcmToken, string id, DateTimeOffset end, TimeSpan remaining, CancellationToken ct)
    {
        try
        {
            string accessToken = await GetAccessTokenAsync(account, ct).ConfigureAwait(false);

            var data = new Dictionary<string, string>
            {
                ["id"] = id,
                ["durationMs"] = ((long)remaining.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["endMs"] = end.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            // Data-only + HIGH priority: delivered in Doze, no notification shown. The TTL drops a message that can no longer matter.
            string body = JsonSerializer.Serialize(new
            {
                message = new
                {
                    token = fcmToken,
                    android = new { priority = "HIGH", ttl = $"{(long)Math.Ceiling(remaining.TotalSeconds)}s" },
                    data,
                },
            });

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(account.ProjectId)}/messages:send")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            return await Classify(response, ct).ConfigureAwait(false);
        }
        catch (AuthException ex)
        {
            return new Attempt(ex.Retryable ? AttemptKind.Retry : AttemptKind.Permanent, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _log?.LogWarning("FCM request failed: {Message}", ex.Message);
            return new Attempt(AttemptKind.Retry, "Sem conexão com o FCM");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Attempt(AttemptKind.Retry, "O FCM demorou demais para responder");
        }
    }

    private async Task<Attempt> Classify(HttpResponseMessage response, CancellationToken ct)
    {
        int status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode) return new Attempt(AttemptKind.Delivered, string.Empty);

        string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        _log?.LogWarning("FCM answered {Status}", status);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _accessToken = null; // stale or revoked: fetch a new one on the retry
            return new Attempt(AttemptKind.Retry, "O FCM recusou a autorização");
        }
        if (response.StatusCode == HttpStatusCode.NotFound || text.Contains("UNREGISTERED", StringComparison.Ordinal))
            return new Attempt(AttemptKind.Permanent, "Token do celular expirou: copie de novo no app");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            return new Attempt(AttemptKind.Permanent, "Token do celular inválido");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return new Attempt(AttemptKind.Permanent, "FCM recusou: confira a API e a permissão da chave");
        if (status == 429 || status >= 500)
            return new Attempt(AttemptKind.Retry, "O FCM está indisponível");
        return new Attempt(AttemptKind.Permanent, $"FCM respondeu {status}");
    }

    private async Task<string> GetAccessTokenAsync(ServiceAccount account, CancellationToken ct)
    {
        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_accessToken is not null && _clock() < _accessTokenExpiry) return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, account.TokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = BuildAssertion(account),
                }),
            };
            using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                int status = (int)response.StatusCode;
                _log?.LogWarning("OAuth token request answered {Status}", status);
                throw new AuthException(status == 429 || status >= 500, "Chave do FCM recusada pelo Google");
            }

            using JsonDocument doc = JsonDocument.Parse(text);
            string? token = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(token)) throw new AuthException(false, "Resposta do Google sem access_token");

            int expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int s) ? s : 3600;
            // Stay under an hour and leave a margin so a token never expires mid-request.
            TimeSpan lifetime = TimeSpan.FromSeconds(Math.Clamp(expiresIn - 300, 60, 55 * 60));
            _accessToken = token;
            _accessTokenExpiry = _clock() + lifetime;
            return token;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private string BuildAssertion(ServiceAccount account)
    {
        long iat = _clock().ToUnixTimeSeconds();
        string header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
        string claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = account.ClientEmail,
            scope = Scope,
            aud = account.TokenUri,
            iat,
            exp = iat + 3600,
        }));
        string signingInput = $"{header}.{claims}";

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(account.PrivateKey);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new AuthException(false, "private_key da chave do FCM é inválida");
        }
        byte[] signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private enum AttemptKind { Delivered, Retry, Permanent }

    private readonly record struct Attempt(AttemptKind Kind, string Detail);

    private sealed class AuthException(bool retryable, string message) : Exception(message)
    {
        public bool Retryable { get; } = retryable;
    }

    private sealed record ServiceAccount(string ClientEmail, string PrivateKey, string ProjectId, string TokenUri)
    {
        /// <summary>Null (with a user-facing reason) when the file is missing or incomplete.</summary>
        public static ServiceAccount? Load(string path, out string? error)
        {
            error = null;
            string json;
            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = $"Chave do FCM não encontrada em {path}";
                return null;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                string? email = Read(root, "client_email");
                string? key = Read(root, "private_key");
                string? project = Read(root, "project_id");
                string tokenUri = Read(root, "token_uri") ?? DefaultTokenUri;
                if (email is null || key is null || project is null)
                {
                    error = "Chave do FCM incompleta (client_email, private_key e project_id)";
                    return null;
                }

                // The signed assertion goes to this address: refuse anything that is not Google over HTTPS.
                if (!Uri.TryCreate(tokenUri, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps ||
                    !(uri.Host == "googleapis.com" || uri.Host.EndsWith(".googleapis.com", StringComparison.Ordinal)))
                {
                    error = "token_uri da chave do FCM não é do Google";
                    return null;
                }

                return new ServiceAccount(email, key, project, tokenUri);
            }
            catch (JsonException)
            {
                error = "Arquivo da chave do FCM não é um JSON válido";
                return null;
            }
        }

        private static string? Read(JsonElement root, string name) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;
    }
}
