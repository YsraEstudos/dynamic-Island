using System.Net;
using System.Text;
using Island.Core.Models;
using Island.Windows.Updates;

namespace Island.Windows.Tests;

public sealed class GitHubUpdateServiceTests : IDisposable
{
    private const string Latest = "https://api.github.com/repos/YsraEstudos/dynamic-Island/releases/latest";
    private const string Package = "https://github.com/YsraEstudos/dynamic-Island/releases/download/v0.2.0/DynamicIsland-0.2.0-win-x64.zip";

    private sealed record Seen(string Url, string? UserAgent);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "island-update-" + Guid.NewGuid().ToString("N"));
    private readonly List<Seen> _seen = [];
    private Func<HttpRequestMessage, HttpResponseMessage> _reply = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private GitHubUpdateService Service(string repository = "YsraEstudos/dynamic-Island")
    {
        var handler = new FakeHandler(request =>
        {
            _seen.Add(new Seen(request.RequestUri!.ToString(), request.Headers.UserAgent.ToString()));
            return _reply(request);
        });
        return new GitHubUpdateService(new HttpClient(handler), () => repository, new Version(0, 1, 0), _dir);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Release(string tag) =>
        $"{{\"tag_name\":\"{tag}\",\"assets\":[{{\"name\":\"DynamicIsland-0.2.0-win-x64.zip\",\"browser_download_url\":\"{Package}\"}}]}}";

    private static UpdateInfo Update() => new(new Version(0, 2, 0, 0), "v0.2.0", new Uri(Package));

    [Fact]
    public async Task A_check_finds_a_newer_release_and_caches_it()
    {
        _reply = _ => Json(Release("v0.2.0"));
        var service = Service();

        Assert.Null(service.Available);
        UpdateInfo? update = await service.CheckAsync();

        Assert.NotNull(update);
        Assert.Equal("v0.2.0", update.Tag);
        Assert.Same(update, service.Available);
        Seen seen = Assert.Single(_seen);
        Assert.Equal(Latest, seen.Url);
        Assert.Equal("DynamicIsland-Updater", seen.UserAgent);
    }

    [Fact]
    public async Task A_check_that_finds_the_app_current_clears_the_cached_update()
    {
        _reply = _ => Json(Release("v0.2.0"));
        var service = Service();
        await service.CheckAsync();
        Assert.NotNull(service.Available);

        _reply = _ => Json(Release("v0.1.0"));
        Assert.Null(await service.CheckAsync());
        Assert.Null(service.Available);
    }

    [Fact]
    public async Task A_repository_without_any_release_has_no_update()
    {
        _reply = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        Assert.Null(await Service().CheckAsync());
    }

    [Fact]
    public async Task An_invalid_repository_setting_is_never_requested()
    {
        Assert.Null(await Service(repository: "owner/../../evil").CheckAsync());
        Assert.Empty(_seen);
    }

    [Fact]
    public async Task HTTP_errors_throw_so_the_background_check_can_log_them()
    {
        _reply = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<HttpRequestException>(() => Service().CheckAsync());
    }

    [Fact]
    public async Task A_download_is_saved_in_the_updates_folder_and_reports_progress()
    {
        byte[] payload = [0x50, 0x4B, 0x03, 0x04, .. Enumerable.Repeat((byte)7, 200_000)];
        _reply = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        var reported = new List<double>();

        string path = await Service().DownloadAsync(Update(), new Recorder(reported));

        Assert.Equal(Path.Combine(_dir, "DynamicIsland-0.2.0.0.zip"), path);
        Assert.Equal(payload, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".part"));
        Assert.NotEmpty(reported);
        Assert.Equal(1.0, reported[^1]);
    }

    [Fact]
    public async Task A_download_that_is_not_a_zip_is_discarded()
    {
        _reply = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>sign in</html>") };

        await Assert.ThrowsAsync<InvalidDataException>(() => Service().DownloadAsync(Update()));
        Assert.False(File.Exists(Path.Combine(_dir, "DynamicIsland-0.2.0.0.zip")));
        Assert.False(File.Exists(Path.Combine(_dir, "DynamicIsland-0.2.0.0.zip.part")));
    }

    [Fact]
    public async Task A_download_from_an_untrusted_address_is_refused_before_any_request()
    {
        var update = new UpdateInfo(new Version(0, 2, 0, 0), "v0.2.0", new Uri("https://evil.example.com/a.zip"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().DownloadAsync(update));
        Assert.Empty(_seen);
    }

    private sealed class Recorder(List<double> values) : IProgress<double>
    {
        public void Report(double value) => values.Add(value);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}
