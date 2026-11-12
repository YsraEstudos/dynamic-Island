using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Island.Core.Phone;

/// <summary>
/// One "the phone applied (or failed to apply) the block" confirmation, published by the Android app to its ntfy topic.
/// The payload matches the Kotlin <c>AckClient</c> contract:
/// <c>{"v":1,"id":"...","applied":true,"a11y":true,"admin":true,"level":"full","endMs":0,"ts":0,"gapMs":0}</c>.
/// The block is only really applied when both <see cref="Applied"/> and <see cref="Accessibility"/> are true.
/// </summary>
public sealed record PhoneBlockAck(
    string Id,
    bool Applied,
    bool Accessibility,
    bool Admin,
    string? Level,
    DateTimeOffset? End,
    DateTimeOffset? Timestamp,
    string? Reason = null)
{
    /// <summary>Domain separation prefix, shared with the Android app. Never change it alone: the phone would stop matching.</summary>
    public const string TopicSalt = "dynamic-island-ack-v1:";

    /// <summary>Longest ack id accepted, so a hostile ntfy payload cannot grow memory without bound.</summary>
    private const int MaxIdLength = 128;

    private const int MaxJsonLength = 8 * 1024;

    /// <summary>
    /// True for the guardian service's heartbeats (<c>reason:"guardian"</c> / <c>"guardian-revive"</c>). The guardian
    /// watches accessibility from inside the phone app and does not know the FCM message id, so these acks arrive
    /// without one and still prove the phone is alive and protected. Any reason starting with "guardian" counts, so the
    /// Android side can add new guardian events without breaking the island.
    /// </summary>
    public bool IsGuardianStatus => Reason is not null
        && Reason.Trim().StartsWith("guardian", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The ntfy topic the phone publishes its acks to: "di-ack-" followed by the first 32 lowercase hex characters of
    /// SHA-256(<see cref="TopicSalt"/> + FCM token). Deriving it from the FCM token means both ends agree without
    /// sharing a second secret: knowing the topic requires the token, and the token already authorizes the FCM send.
    /// </summary>
    public static string TopicFor(string fcmToken)
    {
        ArgumentNullException.ThrowIfNull(fcmToken);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(TopicSalt + fcmToken.Trim()));
        return "di-ack-" + Convert.ToHexStringLower(hash)[..32];
    }

    /// <summary>True when the phone says the block is in effect.</summary>
    public bool IsConfirmed => Applied && Accessibility;

    /// <summary>User-facing reason the block is not applied, or null when it is.</summary>
    public static string? FailureReason(bool applied, bool accessibility) => (applied, accessibility) switch
    {
        (true, true) => null,
        (false, false) => "o celular não aplicou o bloqueio e a acessibilidade está desligada",
        (false, true) => "o celular não aplicou o bloqueio",
        _ => "a acessibilidade está desligada no celular",
    };

    /// <summary>Reason this ack does not confirm the block, or null when it does.</summary>
    public string? FailureReason() => FailureReason(Applied, Accessibility);

    /// <summary>
    /// Parses the ack payload itself (the JSON string carried inside the ntfy "message" field). Never throws.
    /// A missing <c>a11y</c> counts as false: an older or forged ack must never confirm a block by omission.
    /// The <c>id</c> is required for an app ack; a guardian heartbeat may omit it because the guardian never sees the
    /// FCM message id (it is recognized by its <c>reason</c>).
    /// </summary>
    public static bool TryParse(string? payload, [NotNullWhen(true)] out PhoneBlockAck? ack)
    {
        ack = null;
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaxJsonLength) return false;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payload);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            string? reason = ReadString(root, "reason");
            bool guardian = reason is not null
                && reason.Trim().StartsWith("guardian", StringComparison.OrdinalIgnoreCase);
            string? id = ReadString(root, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                if (!guardian) return false; // a block confirmation must identify its message
                id = string.Empty;           // guardian heartbeat: matched by reason instead of by message id
            }
            else
            {
                id = id.Trim();
            }
            if (id.Length > MaxIdLength) return false;

            if (!TryReadBool(root, "applied", out bool applied)) return false;
            bool accessibility = TryReadBool(root, "a11y", out bool a11y) && a11y;
            bool admin = TryReadBool(root, "admin", out bool adminValue) && adminValue;

            ack = new PhoneBlockAck(
                id,
                applied,
                accessibility,
                admin,
                ReadString(root, "level"),
                ReadEpoch(root, "endMs"),
                ReadEpoch(root, "ts") ?? ReadEpoch(root, "time"),
                reason);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses one line of the ntfy <c>/json</c> stream. Only "message" events can carry an ack; "open",
    /// "keepalive" and "poll_request" lines are ignored. Never throws.
    /// </summary>
    public static bool TryParseNtfyLine(string? line, [NotNullWhen(true)] out PhoneBlockAck? ack)
    {
        ack = null;
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxJsonLength * 2) return false;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (root.TryGetProperty("event", out JsonElement kind) && kind.ValueKind == JsonValueKind.String
                && !string.Equals(kind.GetString(), "message", StringComparison.Ordinal))
            {
                return false;
            }

            return root.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.String
                && TryParse(message.GetString(), out ack);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Accepts JSON true/false and the numbers 1/0 (some bridges serialize booleans as numbers).</summary>
    private static bool TryReadBool(JsonElement root, string name, out bool result)
    {
        result = false;
        if (!root.TryGetProperty(name, out JsonElement value)) return false;

        switch (value.ValueKind)
        {
            case JsonValueKind.True: result = true; return true;
            case JsonValueKind.False: return true;
            case JsonValueKind.Number when value.TryGetInt64(out long number): result = number != 0; return true;
            case JsonValueKind.String:
                string? text = value.GetString();
                if (bool.TryParse(text, out result)) return true;
                if (text is "0" or "1") { result = text == "1"; return true; }
                return false;
            default:
                return false;
        }
    }

    /// <summary>Epoch milliseconds, the unit the Android app sends; also accepts a numeric string.</summary>
    private static DateTimeOffset? ReadEpoch(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)) return null;

        long milliseconds;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetInt64(out milliseconds)) return null;
        }
        else if (value.ValueKind == JsonValueKind.String
                 && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
        {
            milliseconds = parsed;
        }
        else
        {
            return null;
        }

        // 1970..2100: anything outside is a bug or a hostile payload and DateTimeOffset would throw.
        if (milliseconds < 0 || milliseconds > 4_102_444_800_000L) return null;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
