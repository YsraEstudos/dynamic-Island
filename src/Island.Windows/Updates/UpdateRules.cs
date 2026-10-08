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

    /// <summary>Missing parts count as zero, so "0.2" and "0.2.0.0" compare equal. MSIX identities use four parts.</summary>
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
    /// and its .msix asset has a trusted address; otherwise null. Throws <see cref="JsonException"/> on invalid JSON.
    /// </summary>
    public static UpdateInfo? Evaluate(string json, Version current)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        string? tag = ReadString(root, "tag_name");
        if (!TryParseTag(tag, out Version version)) return null;
        if (version.CompareTo(Normalize(current)) <= 0) return null;

        Uri? download = FindMsixAsset(root);
        return download is null ? null : new UpdateInfo(version, tag!.Trim(), download);
    }

    /// <summary>The first asset whose name ends in .msix. Null when there is none, or when its address is not trusted.</summary>
    public static Uri? FindMsixAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;

            string? name = ReadString(asset, "name");
            if (name is null || !name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)) continue;

            string? url = ReadString(asset, "browser_download_url");
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && IsTrustedDownload(uri) ? uri : null;
        }

        return null;
    }

    /// <summary>An MSIX is a ZIP container, so the file must start with the local file header signature "PK\x03\x04".</summary>
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
    /// The script that installs the package, logs the outcome and, once the update replaced the app, starts the new copy
    /// through its Start-menu entry (package family name + application id). The relaunch is skipped when the package is not installed.
    /// </summary>
    public static string BuildInstallScript(string package, string logPath, string packageName, string appId) =>
        string.Join("\r\n",
            "$ErrorActionPreference = 'Stop'",
            $"function Log($m) {{ Add-Content -LiteralPath {Quote(logPath)} -Value ((Get-Date -Format o) + ' ' + $m) }}",
            "try {",
            $"  Add-AppxPackage -Path {Quote(package)} -ForceUpdateFromAnyVersion",
            "  Log 'installed'",
            $"  $pkg = Get-AppxPackage -Name {Quote(packageName)} | Select-Object -First 1",
            $"  if ($pkg) {{ Start-Process explorer.exe -ArgumentList ('shell:AppsFolder\\' +$pkg.PackageFamilyName + '!' + {Quote(appId)}); Log 'relaunched' }}",
            "} catch { Log $_.Exception.Message }",
            "");

    /// <summary>
    /// A one-line command that asks WMI to start <paramref name="scriptPath"/>. A process created that way is not a child of the app,
    /// so replacing the package (which ends the app and its children) does not stop the installer.
    /// </summary>
    public static string BuildDetachedLauncher(string scriptPath)
    {
        string inner = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"";
        return $"Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{{ CommandLine = {Quote(inner)} }} | Out-Null";
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
