using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Island.Core.Progress;

/// <summary>
/// Turns one line of the progress-link protocol (docs/progress-link.md) into a <see cref="ProgressUpdate"/>. Never
/// throws: a malformed line, another protocol version, an unknown state or a line over the limit is just rejected.
/// Unknown fields are ignored.
/// </summary>
public static class ProgressMessageParser
{
    /// <summary>Longest line accepted, in chars. Longer lines are rejected whole.</summary>
    public const int MaxLineChars = 4096;

    private const int ProtocolVersion = 1;
    private const int MaxIdChars = 64;
    private const int MaxTitleChars = 80;
    private const int MaxMessageChars = 120;
    private const int MaxLabelChars = 40;

    public static bool TryParse(ReadOnlySpan<char> line, [NotNullWhen(true)] out ProgressUpdate? update)
    {
        update = null;
        if (line.IsEmpty || line.Length > MaxLineChars) return false;

        byte[] utf8 = new byte[Encoding.UTF8.GetByteCount(line)];
        Encoding.UTF8.GetBytes(line, utf8);

        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("v", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out int v)
                || v != ProtocolVersion)
            {
                return false;
            }

            string? id = ReadString(root, "id");
            if (string.IsNullOrWhiteSpace(id) || id.Length > MaxIdChars) return false;

            string? title = Clean(ReadString(root, "title"), MaxTitleChars);
            if (title is null) return false;

            ProgressState? state = ParseState(ReadString(root, "state"));
            if (state is null) return false;

            update = new ProgressUpdate(
                id,
                title,
                Clean(ReadString(root, "category"), MaxLabelChars),
                state.Value,
                ReadPercent(root),
                Clean(ReadString(root, "message"), MaxMessageChars),
                Clean(ReadString(root, "source"), MaxLabelChars));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ProgressState? ParseState(string? text) => text switch
    {
        "queued" => ProgressState.Queued,
        "running" => ProgressState.Running,
        "done" => ProgressState.Done,
        "error" => ProgressState.Error,
        "cancelled" => ProgressState.Cancelled,
        _ => null,
    };

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>0..100, clamped. Missing, null, non-numeric or non-finite values mean "unknown".</summary>
    private static double? ReadPercent(JsonElement root)
    {
        if (!root.TryGetProperty("percent", out JsonElement value) || value.ValueKind != JsonValueKind.Number) return null;
        if (!value.TryGetDouble(out double percent) || !double.IsFinite(percent)) return null;
        return Math.Clamp(percent, 0.0, 100.0);
    }

    /// <summary>Trims, cuts to <paramref name="max"/> chars without splitting a surrogate pair, and maps empty to null.</summary>
    private static string? Clean(string? text, int max)
    {
        string? trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length <= max) return trimmed;

        int cut = max;
        if (char.IsHighSurrogate(trimmed[cut - 1])) cut--;
        return trimmed[..cut];
    }
}
