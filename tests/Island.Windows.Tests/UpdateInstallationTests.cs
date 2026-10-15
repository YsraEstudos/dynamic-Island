using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Island.Core.Models;
using Island.Windows.Updates;

namespace Island.Windows.Tests;

public sealed class UpdateInstallationTests : IDisposable
{
    private const string Repository = "YsraEstudos/dynamic-Island";
    private const string Package = "https://github.com/" + Repository + "/releases/download/v9.0.0/app.zip";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "island-install-" + Guid.NewGuid().ToString("N"));
    private int _launched;
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static byte[] Zip()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry(Path.GetFileName(Environment.ProcessPath)!).Open()))
            writer.Write("test executable placeholder");
        return stream.ToArray();
    }

    private GitHubUpdateService Service(byte[] zip) => new(new HttpClient(new Handler(zip)), () => Repository,
        new Version(0, 1), _dir, launchInstaller: _ => _launched++);

    private static UpdateInfo Update(byte[] zip) => new(new Version(9, 0, 0, 0), "v9.0.0", new Uri(Package),
        Convert.ToHexString(SHA256.HashData(zip)));

    [Fact]
    public async Task Failed_final_save_never_launches_and_installation_can_be_retried()
    {
        byte[] zip = Zip();
        using var service = Service(zip);
        bool called = false;
        Assert.False(await service.InstallAsync(Update(zip), beforeLaunch: () => { called = true; return Task.FromResult(false); }));
        Assert.True(called);
        Assert.Equal(0, _launched);
        Assert.True(await service.InstallAsync(Update(zip), beforeLaunch: () => Task.FromResult(true)));
        Assert.Equal(1, _launched);
    }

    [Fact]
    public async Task Throwing_final_save_never_launches()
    {
        byte[] zip = Zip();
        using var service = Service(zip);
        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(Update(zip), beforeLaunch: () => throw new IOException("save failed")));
        Assert.Equal(0, _launched);
    }

    [Fact]
    public async Task Concurrent_request_does_not_launch_or_claim_to_have_launched()
    {
        byte[] zip = Zip();
        using var service = Service(zip);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> first = service.InstallAsync(Update(zip), beforeLaunch: () => { entered.SetResult(); return release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await service.InstallAsync(Update(zip)));
        release.SetResult(true);
        Assert.True(await first);
        Assert.Equal(1, _launched);
    }

    [Fact]
    public async Task Incorrect_digest_discards_download_before_launch()
    {
        byte[] zip = Zip();
        using var service = Service(zip);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(Update(zip) with { Sha256 = new string('0', 64) }));
        Assert.Equal(0, _launched);
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));
        Assert.Empty(Directory.GetFiles(_dir, "*.zip"));
    }

    [Fact]
    public async Task Another_repository_is_refused_before_download()
    {
        using var service = Service(Zip());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync(new UpdateInfo(new Version(9, 0), "v9",
            new Uri("https://github.com/other/repo/releases/download/v9/app.zip"))));
        Assert.False(Directory.Exists(_dir));
    }

    private sealed class Handler(byte[] zip) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
    }
}
