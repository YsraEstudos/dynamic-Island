using System.Diagnostics;
using System.Net;
using System.Reflection;
using Island.Core.Abstractions;
using Island.Core.Models;
using Microsoft.Extensions.Logging;

namespace Island.Windows.Updates;

/// <summary>
/// Looks for a newer release of the app on GitHub and installs its .msix. The check runs shortly after startup and then
/// every six hours; the result is cached in <see cref="Available"/> so the context menu reads it without I/O.
/// Install downloads the package to %LocalAppData%\DynamicIsland\updates, then starts a hidden PowerShell that runs
/// Add-AppxPackage. That update replaces the running app, and the script then reopens it. Only trusted GitHub addresses are downloaded from.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService, IDisposable
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private const string UserAgent = "DynamicIsland-Updater";
    private const string PackageName = "DynamicIsland"; // Identity Name in packaging/Package.appxmanifest
    private const string AppId = "DynamicIsland";       // Application Id in the same manifest
    private const int BufferSize = 81920;
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);

    private static readonly string InstallLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "logs", "update-install.log");

    private readonly HttpClient _http;
    private readonly Func<string> _repository;
    private readonly Version _current;
    private readonly string _downloadDirectory;
    private readonly ILogger? _log;
    private readonly CancellationTokenSource _stop = new();

    private volatile UpdateInfo? _available;
    private int _started;
    private int _installing;

    /// <param name="http">Client for the API and downloads. Its timeout should be infinite, since each request sets its own.</param>
    /// <param name="repository">Read at each check, so a settings change applies without a restart.</param>
    /// <param name="currentVersion">The running version; see <see cref="RunningVersion"/>.</param>
    /// <param name="downloadDirectory">Defaults to %LocalAppData%\DynamicIsland\updates.</param>
    public GitHubUpdateService(HttpClient http, Func<string> repository, Version currentVersion,
        string? downloadDirectory = null, ILogger? log = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _current = UpdateRules.Normalize(currentVersion);
        _downloadDirectory = downloadDirectory ?? DefaultDownloadDirectory;
        _log = log;
    }

    /// <summary>%LocalAppData%\DynamicIsland\updates</summary>
    public static string DefaultDownloadDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "updates");

    /// <summary>The single client for this service: short connect timeout, idle connections closed after 30 s.</summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        ConnectTimeout = TimeSpan.FromSeconds(10),
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>The app's informational version ("0.1.0", any "+metadata" dropped), normalised to four parts.</summary>
    public static Version RunningVersion(Assembly assembly)
    {
        string? info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (UpdateRules.TryParseTag(info?.Split('+')[0], out Version version)) return version;

        return UpdateRules.Normalize(assembly.GetName().Version ?? new Version(0, 0, 0, 0));
    }

    /// <inheritdoc />
    public UpdateInfo? Available => _available;

    /// <inheritdoc />
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _ = RunInBackgroundAsync(_stop.Token);
    }

    /// <summary>Stops the background checks. An install already under way is left alone.</summary>
    public void Dispose() => _stop.Cancel();

    /// <inheritdoc />
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        string repository = _repository().Trim();
        if (!UpdateRules.IsValidRepository(repository))
        {
            _log?.LogWarning("Update repository '{Repository}' is not owner/name; update check skipped", repository);
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CheckTimeout);

        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _available = null; // The repository has no published release yet.
            return null;
        }
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        UpdateInfo? update = UpdateRules.Evaluate(json, _current);
        _available = update;
        if (update is not null) _log?.LogInformation("Update {Tag} is available", update.Tag);
        return update;
    }

    /// <inheritdoc />
    public async Task InstallAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (Interlocked.Exchange(ref _installing, 1) == 1) return; // Already downloading or installing.

        bool launched = false;
        try
        {
            string package = await DownloadAsync(update, progress, ct).ConfigureAwait(false);
            LaunchInstaller(package);
            launched = true; // Stays set: the running app is about to be replaced.
        }
        finally
        {
            if (!launched) Interlocked.Exchange(ref _installing, 0);
        }
    }

    /// <summary>Downloads the package into the updates folder and returns its path. Refuses untrusted addresses and files that are not ZIP containers.</summary>
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!UpdateRules.IsTrustedDownload(update.DownloadUrl))
            throw new InvalidOperationException("The update address is not a GitHub download.");

        Directory.CreateDirectory(_downloadDirectory);
        string target = Path.Combine(_downloadDirectory, $"DynamicIsland-{update.Version}.msix");
        string partial = target + ".part";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            await using (Stream source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var sink = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                byte[] buffer = new byte[BufferSize];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await sink.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total is > 0) progress?.Report(Math.Min(1.0, (double)done / total.Value));
                }
            }

            if (!UpdateRules.LooksLikeZip(partial)) throw new InvalidDataException("The downloaded file is not an MSIX package.");

            File.Move(partial, target, overwrite: true);
            _log?.LogInformation("Downloaded update {Tag} to {Path}", update.Tag, target);
            return target;
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private async Task RunInBackgroundAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(StartupDelay, ct).ConfigureAwait(false);
            using var timer = new PeriodicTimer(CheckInterval);
            do
            {
                await CheckQuietlyAsync(ct).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The app is shutting down.
        }
    }

    /// <summary>One background check. Failures are logged, never thrown: a missing network must not affect the island.</summary>
    private async Task CheckQuietlyAsync(CancellationToken ct)
    {
        try
        {
            await CheckAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Update check failed");
        }
    }

    /// <summary>
    /// Writes the install script and starts it detached from this app (see <see cref="UpdateRules.BuildDetachedLauncher"/>).
    /// It installs the package, logs the outcome and reopens the new app, since this one is replaced.
    /// </summary>
    private void LaunchInstaller(string package)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InstallLogPath)!);
        Directory.CreateDirectory(_downloadDirectory);
        string scriptPath = Path.Combine(_downloadDirectory, "install-update.ps1");
        File.WriteAllText(scriptPath, UpdateRules.BuildInstallScript(package, InstallLogPath, PackageName, AppId));

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-WindowStyle");
        start.ArgumentList.Add("Hidden");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(UpdateRules.BuildDetachedLauncher(scriptPath));

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell did not start.");
        _log?.LogInformation("Installer started for {Package}", package);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a partial download.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: a leftover .part file is harmless.
        }
    }
}
