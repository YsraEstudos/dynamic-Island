using System.Text.Json;
using Island.Core.Models;

namespace Island.Windows.Updates;

/// <summary>Pure rules for the update check: tag and version parsing, asset choice, and which download addresses are trusted.</summary>
internal static class UpdateRules
{
    /// <summary>"v0.2.0" or "0.2.0" becomes a four-part version. Pre-release tags and anything else are rejected.</summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        string text = tag.Trim();
        if (text[0] is 'v' or 'V') text = text[1..];
        if (!Version.TryParse(text, out Version? parsed)) return false;

        version = Normalize(parsed);
        return true;
    }

    /// <summary>Missing parts count as zero, so "0.2" and "0.2.0.0" compare equal.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>"owner/name" made of GitHub's characters only, so it can go into the API path unchanged.</summary>
    public static bool IsValidRepository(string? repository)
    {
        if (string.IsNullOrEmpty(repository)) return false;

        string[] parts = repository.Split('/');
        return parts.Length == 2 && parts.All(IsRepositorySegment);
    }

    private static bool IsRepositorySegment(string part) =>
        part is not ("." or "..") && part.Length is >= 1 and <= 100 &&
        part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    /// <summary>HTTPS on github.com or its download host only. Anything else is refused before a byte is fetched.</summary>
    public static bool IsTrustedDownload(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        (uri.Host == "github.com" || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads a GitHub "latest release" payload. Returns the update when the release is newer than <paramref name="current"/>
    /// and its .zip asset has a trusted address; otherwise null. Throws <see cref="JsonException"/> on invalid JSON.
    /// </summary>
    public static UpdateInfo? Evaluate(string json, Version current)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        string? tag = ReadString(root, "tag_name");
        if (!TryParseTag(tag, out Version version)) return null;
        if (version.CompareTo(Normalize(current)) <= 0) return null;

        Uri? download = FindPackageAsset(root);
        return download is null ? null : new UpdateInfo(version, tag!.Trim(), download);
    }

    /// <summary>The first asset whose name ends in .zip. Null when there is none, or when its address is not trusted.</summary>
    public static Uri? FindPackageAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;

            string? name = ReadString(asset, "name");
            if (name is null || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

            string? url = ReadString(asset, "browser_download_url");
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && IsTrustedDownload(uri) ? uri : null;
        }

        return null;
    }

    /// <summary>The package is a ZIP file, so it must start with the local file header signature "PK\x03\x04".</summary>
    public static bool LooksLikeZip(string path)
    {
        using FileStream stream = File.OpenRead(path);
        Span<byte> head = stackalloc byte[4];
        return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == 4 &&
               head[0] == 'P' && head[1] == 'K' && head[2] == 3 && head[3] == 4;
    }

    /// <summary>A PowerShell single-quoted literal: the only character needing an escape is the quote itself, doubled.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>
    /// The script that finishes an update after the app exited: waits for the old process, copies the new files over the
    /// install folder, logs the outcome and starts the app again. An app instance that hung while closing (a WPF
    /// shutdown deadlock) would keep its files locked forever, so every instance still running from the install folder is
    /// stopped before the copy. The app is reopened even when the copy fails, so an update never leaves it closed.
    /// </summary>
    public static string BuildUpdateScript(int processId, string sourceDir, string installDir, string exePath, string logPath) =>
        string.Join("\r\n",
            "$ErrorActionPreference = 'Stop'",
            $"function Log($m) {{ Add-Content -LiteralPath {Quote(logPath)} -Value ((Get-Date -Format o) + ' ' + $m) }}",
            "try {",
            $"  Wait-Process -Id {processId} -Timeout 20 -ErrorAction SilentlyContinue",
            $"  Get-Process -ErrorAction SilentlyContinue | Where-Object {{ $_.Path -eq {Quote(exePath)} }} | Stop-Process -Force -ErrorAction SilentlyContinue",
            "  Start-Sleep -Milliseconds 500",
            $"  robocopy {Quote(sourceDir)} {Quote(installDir)} /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null",
            "  if ($LASTEXITCODE -ge 8) { throw ('robocopy failed with code ' + $LASTEXITCODE) }",
            "  Log 'installed'",
            "} catch { Log $_.Exception.Message }",
            "try {",
            $"  Start-Process -FilePath {Quote(exePath)}",
            "  Log 'relaunched'",
            "} catch { Log $_.Exception.Message }",
            "");

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
