using System.IO.Compression;
using Island.Core.Files;
using PdfSharp.Pdf.IO;
using ImageMagick;

namespace Island.Core.Tests;

public sealed class FileMediaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orbit-media-" + Guid.NewGuid().ToString("N"));
    public FileMediaTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Real_images_convert_and_create_pdf_and_contact_sheet()
    {
        string image = Path.Combine(_root, "image.png");
        using (var fixture = new MagickImage(MagickColors.ForestGreen, 128, 96)) fixture.Write(image);
        var service = new FileActionService();
        string secondImage = Path.Combine(_root, "second.png");
        File.Copy(image, secondImage);
        foreach (string format in new[] { "jpg", "webp", "png" })
        {
            string output = Path.Combine(_root, format);
            await service.RunAsync(FileAction.Images, [image], output, new() { Format = format, MaxWidth = 64 });
            Assert.True(new FileInfo(Path.Combine(output, "image." + format)).Length > 0);
        }
        // Optional real HEIC input, supplied locally so ordinary tests never depend on a network download.
        if (Environment.GetEnvironmentVariable("FILE_ORBIT_HEIC_SAMPLE") is { Length: > 0 } heicSample)
        {
            string heicOutput = Path.Combine(_root, "from-heic");
            await service.RunAsync(FileAction.Images, [heicSample], heicOutput, new() { Format = "jpg" });
            Assert.True(new FileInfo(Directory.GetFiles(heicOutput, "*.jpg").Single()).Length > 0);
        }
        foreach (var action in new[] { FileAction.ImagesPdf, FileAction.ContactSheet })
        {
            string output = Path.Combine(_root, action.ToString());
            await service.RunAsync(action, [image, secondImage], output, new());
            using var pdf = PdfReader.Open(Directory.GetFiles(output, "*.pdf").Single(), PdfDocumentOpenMode.Import);
            Assert.Equal(action == FileAction.ImagesPdf ? 2 : 1, pdf.PageCount);
        }
        string reportOutput = Path.Combine(_root, "report");
        await service.RunAsync(FileAction.Index, [image], reportOutput, new());
        Assert.Contains("data:image/jpeg;base64,", await File.ReadAllTextAsync(Path.Combine(reportOutput, "indice.html")));
        string duplicatesOutput = Path.Combine(_root, "dupes");
        await service.RunAsync(FileAction.Duplicates, [image, secondImage], duplicatesOutput, new());
        Assert.Contains("≈", await File.ReadAllTextAsync(Path.Combine(duplicatesOutput, "duplicados.html")));
    }

    [RequiresFfmpeg]
    public async Task Real_video_and_audio_conversions_generate_decodable_outputs()
    {
        string ffmpeg = FileTools.Find("ffmpeg") ?? throw new InvalidOperationException("FFmpeg necessário para este teste de integração.");
        string source = Path.Combine(_root, "source.mp4");
        await FileTools.RunAsync(ffmpeg, ["-v", "error", "-f", "lavfi", "-i", "testsrc=size=128x96:rate=12", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "0.5", "-pix_fmt", "yuv420p", "-c:v", "libx264", "-c:a", "aac", source], default);
        var service = new FileActionService();
        foreach (string format in new[] { "mp4", "mov", "webm", "gif", "jpg", "mp3" })
        {
            string output = Path.Combine(_root, "v-" + format);
            await service.RunAsync(FileAction.Video, [source], output, new() { Format = format, MaxWidth = 64, DurationSeconds = .25 });
            string result = Directory.GetFiles(output).Single();
            await FileTools.RunAsync(ffmpeg, ["-v", "error", "-i", result, "-f", "null", "-"], default);
        }
        string audio = Path.Combine(_root, "v-mp3", "source.mp3");
        foreach (string format in new[] { "mp3", "wav", "m4a", "ogg" })
        {
            string output = Path.Combine(_root, "a-" + format);
            await service.RunAsync(FileAction.Audio, [audio], output, new() { Format = format, StartSeconds = .02, DurationSeconds = .1 });
            await FileTools.RunAsync(ffmpeg, ["-v", "error", "-i", Directory.GetFiles(output).Single(), "-f", "null", "-"], default);
        }
    }

    [Fact]
    public async Task Zip_preserves_folder_paths_and_roundtrips_bytes()
    {
        string folder = Path.Combine(_root, "source"); Directory.CreateDirectory(Path.Combine(folder, "nested"));
        await File.WriteAllTextAsync(Path.Combine(folder, "nested", "a.txt"), "conteúdo");
        string output = Path.Combine(_root, "zip"); var service = new FileActionService();
        await service.RunAsync(FileAction.Package, [folder], output, new() { Name = "envio" });
        string zip = Path.Combine(output, "envio.zip");
        using (var archive = ZipFile.OpenRead(zip)) Assert.NotNull(archive.GetEntry("source/nested/a.txt"));
        string extracted = Path.Combine(_root, "extracted");
        await service.RunAsync(FileAction.Extract, [zip], extracted, new());
        Assert.Equal("conteúdo", await File.ReadAllTextAsync(Path.Combine(extracted, "envio", "source", "nested", "a.txt")));
    }

    [Fact]
    public async Task Nonempty_output_and_output_inside_input_are_rejected()
    {
        string input = Path.Combine(_root, "a.txt"); await File.WriteAllTextAsync(input, "preserve");
        var service = new FileActionService();
        await Assert.ThrowsAsync<IOException>(() => service.RunAsync(FileAction.Rename, [input], _root, new()));
        await Assert.ThrowsAsync<IOException>(() => service.RunAsync(FileAction.Package, [_root], Path.Combine(_root, "out"), new()));
        Assert.Equal("preserve", await File.ReadAllTextAsync(input));
    }

    [Fact]
    public async Task Cancellation_leaves_original_file_untouched()
    {
        string input = Path.Combine(_root, "a.txt"); await File.WriteAllTextAsync(input, "preserve");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileActionService().RunAsync(FileAction.Rename, [input], Path.Combine(_root, "out"), new(), token: cancelled.Token));
        Assert.Equal("preserve", await File.ReadAllTextAsync(input));
    }
    public void Dispose() => Directory.Delete(_root, true);
}

public sealed class RequiresFfmpegAttribute : FactAttribute
{
    public RequiresFfmpegAttribute()
    {
        if (FileTools.Find("ffmpeg") is null) Skip = "Integração de áudio/vídeo requer FFmpeg no PATH ou tools.";
    }
}
