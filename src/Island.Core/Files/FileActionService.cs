using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Island.Core.Files;

/// <summary>Local, copy-on-write file workflows. The caller owns the new output directory.</summary>
public sealed class FileActionService
{
    public static string SafeName(string name)
    {
        string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || "<>:\"/\\|?*".Contains(c) || char.IsControl(c) ? '_' : c)).Trim().TrimEnd('.');
        if (safe.Length > 100) safe = safe[..100];
        if (string.IsNullOrWhiteSpace(safe) || safe is "." or "..") safe = "arquivo";
        if (Regex.IsMatch(safe, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)", RegexOptions.IgnoreCase)) safe = "_" + safe;
        return safe;
    }

    public static string UniquePath(string directory, string name)
    {
        string path = Path.Combine(directory, name); int n = 2;
        while (File.Exists(path) || Directory.Exists(path)) path = Path.Combine(directory,
            $"{Path.GetFileNameWithoutExtension(name)} ({n++}){Path.GetExtension(name)}");
        return path;
    }

    public static string RenamePreview(string path, int index, string pattern)
    {
        string name = pattern.Replace("{name}", Path.GetFileNameWithoutExtension(path))
            .Replace("{date}", File.GetLastWriteTime(path).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Replace("{content}", ContentName(path));
        name = Regex.Replace(name, @"\{n(?::(0{1,8}))?\}", m => index.ToString(m.Groups[1].Success ? m.Groups[1].Value : "0", CultureInfo.InvariantCulture));
        return SafeName(name) + Path.GetExtension(path);
    }

    private static string ContentName(string path)
    {
        if (!FileActionCatalog.Has(path, ".txt", ".md", ".csv", ".json", ".log")) return Path.GetFileNameWithoutExtension(path);
        using var reader = new StreamReader(path); char[] buffer = new char[160]; int length = reader.Read(buffer, 0, buffer.Length);
        return SafeName(new string(buffer, 0, length).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "arquivo");
    }

    public static IReadOnlyList<string> Expand(IReadOnlyList<string> inputs, CancellationToken token)
    {
        var files = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string path)
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
            if (Directory.Exists(path)) foreach (string child in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.OrdinalIgnoreCase)) Visit(child);
            else if (File.Exists(path) && seen.Add(Path.GetFullPath(path)))
            {
                if (files.Count >= 20000) throw new InvalidDataException("Limite de 20.000 arquivos por lote. Divida a pasta em lotes menores.");
                files.Add(path);
            }
        }
        foreach (string path in inputs) Visit(path);
        return files;
    }

    public Task RunAsync(FileAction action, IReadOnlyList<string> inputs, string output, FileActionOptions options,
        IProgress<string>? progress = null, CancellationToken token = default) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Escolha uma pasta de saída vazia.");
        string outputPrefix = output.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (inputs.Any(p => Directory.Exists(p) && outputPrefix.StartsWith(Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("A saída deve ficar fora das pastas de entrada.");
        Directory.CreateDirectory(output);
        string staging = Path.Combine(output, ".island-work-" + Guid.NewGuid().ToString("N"));
        try
        {
            await RunToDirectoryAsync(action, inputs, staging, options, progress, token);
            token.ThrowIfCancellationRequested();
            // Publish only complete artifacts. A failed/cancelled conversion cannot masquerade as a result.
            foreach (string path in Directory.EnumerateFileSystemEntries(staging))
            {
                string target = Path.Combine(output, Path.GetFileName(path));
                if (Directory.Exists(path)) Directory.Move(path, target);
                else File.Move(path, target);
            }
        }
        finally
        {
            // This private, random directory is always a direct child of the caller's output directory.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }, token);

    private Task RunToDirectoryAsync(FileAction action, IReadOnlyList<string> inputs, string output, FileActionOptions options,
        IProgress<string>? progress = null, CancellationToken token = default) => Task.Run(async () =>
    {
        if (FileTools.MissingFor(action) is { } missing) throw new InvalidOperationException(missing);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Escolha uma pasta de saída vazia.");
        string outputFull = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (inputs.Any(p => Directory.Exists(p) && outputFull.StartsWith(Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("A saída deve ficar fora das pastas de entrada.");
        var files = Expand(inputs, token);
        if (files.Count == 0) throw new InvalidDataException("Nenhum arquivo encontrado.");
        if (action is not (FileAction.Rename or FileAction.Duplicates or FileAction.Package or FileAction.Index)
            && !FileActionCatalog.For(files).Contains(action)) throw new InvalidDataException("O lote contém formatos incompatíveis com esta ação.");
        Directory.CreateDirectory(output);
        progress?.Report($"Preparando {files.Count} arquivos…");
        switch (action)
        {
            case FileAction.Package: await Package(inputs, output, options, token); return;
            case FileAction.Duplicates: await DuplicateReport(files, output, options, progress, token); return;
            case FileAction.Index: await Index(files, output, token); return;
            case FileAction.MergePdf:
                using (var merged = new PdfDocument())
                {
                    foreach (string file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        using var doc = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                        foreach (var page in doc.Pages) merged.AddPage(page);
                    }
                    merged.Save(Path.Combine(output, "documentos.pdf"));
                }
                return;
            case FileAction.ImagesPdf: case FileAction.ContactSheet:
                await ImagePdf(files, output, action == FileAction.ContactSheet, options, progress, token); return;
        }
        int index = 0;
        foreach (string file in files)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"{++index}/{files.Count} · {Path.GetFileName(file)}");
            string stem = SafeName(Path.GetFileNameWithoutExtension(file));
            string Target(string ext) => UniquePath(output, stem + "." + ext);
            switch (action)
            {
                case FileAction.Rename:
                    await Copy(file, UniquePath(output, RenamePreview(file, index, options.RenamePattern)), token); break;
                case FileAction.Extract:
                    await Extract(file, UniquePath(output, stem), token); break;
                case FileAction.CsvToXlsx: FileDataConversions.CsvToXlsx(file, Target("xlsx"), token); break;
                case FileAction.JsonToCsv: FileDataConversions.JsonToCsv(file, Target("csv")); break;
                case FileAction.SplitPdf:
                    using (var pdf = PdfReader.Open(file, PdfDocumentOpenMode.Import))
                        for (int p = 0; p < pdf.PageCount; p++)
                        {
                            token.ThrowIfCancellationRequested(); using var part = new PdfDocument(); part.AddPage(pdf.Pages[p]);
                            part.Save(UniquePath(output, $"{stem}-{p + 1:000}.pdf"));
                        }
                    break;
                case FileAction.CompressPdf:
                    await FileTools.RunAsync(FileTools.Find("gswin64c")!, ["-dSAFER", "-dBATCH", "-dNOPAUSE", "-sDEVICE=pdfwrite", "-dCompatibilityLevel=1.6", "-dPDFSETTINGS=/ebook", "-sOutputFile=" + Target("pdf"), "-f", Path.GetFullPath(file)], token); break;
                case FileAction.OfficePdf:
                    // A fresh directory and profile avoid collisions and forwarding into a running office session.
                    string staging = Path.Combine(output, ".office-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
                    try
                    {
                        await FileTools.RunAsync(FileTools.Find("soffice")!, ["-env:UserInstallation=" + new Uri(Path.Combine(staging, "profile")).AbsoluteUri,
                            "--headless", "--convert-to", "pdf", "--outdir", staging, Path.GetFullPath(file)], token);
                        string converted = Path.Combine(staging, Path.GetFileNameWithoutExtension(file) + ".pdf");
                        if (!File.Exists(converted)) throw new IOException("LibreOffice não gerou um PDF para " + Path.GetFileName(file));
                        File.Move(converted, Target("pdf"));
                    }
                    finally { Directory.Delete(staging, true); }
                    break;
                case FileAction.Images: case FileAction.Video: case FileAction.Audio:
                    await ConvertMedia(action, file, Target(ValidatedFormat(action, options.Format)), options, token); break;
            }
        }
    }, token);

    private static async Task Copy(string source, string target, CancellationToken token)
    {
        await using var input = File.OpenRead(source);
        await using var output = new FileStream(target, FileMode.CreateNew);
        await input.CopyToAsync(output, token);
    }

    private static async Task Package(IReadOnlyList<string> inputs, string output, FileActionOptions options, CancellationToken token)
    {
        using var zip = ZipFile.Open(Path.Combine(output, SafeName(options.Name) + ".zip"), ZipArchiveMode.Create);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string input in inputs)
        {
            string rootName = SafeName(Path.GetFileName(input.TrimEnd(Path.DirectorySeparatorChar)));
            string root = rootName; int n = 2;
            while (!names.Add(root)) root = rootName + " (" + n++ + ")";
            var files = Expand([input], token);
            if (Directory.Exists(input) && files.Count == 0) zip.CreateEntry(root + "/");
            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();
                string name = Directory.Exists(input) ? root + "/" + Path.GetRelativePath(input, file).Replace('\\', '/') : root;
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var destination = entry.Open(); await using var source = File.OpenRead(file);
                await source.CopyToAsync(destination, token);
            }
        }
    }

    private static async Task Extract(string source, string output, CancellationToken token)
    {
        using var zip = ZipFile.OpenRead(source);
        const long maxBytes = 10L * 1024 * 1024 * 1024;
        long total = 0;
        if (zip.Entries.Count > 20000) throw new InvalidDataException("ZIP excede 20.000 entradas.");
        string root = Path.GetFullPath(output) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            string target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains(':') ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("ZIP contém caminho externo ou link: " + entry.FullName);
            total = checked(total + entry.Length);
            if (total > maxBytes) throw new InvalidDataException("ZIP excede 10 GB descompactados.");
        }
        Directory.CreateDirectory(output);
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            string target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open(); await using var destination = new FileStream(target, FileMode.CreateNew);
            await input.CopyToAsync(destination, token);
        }
    }

    private static string Html(string s) => WebUtility.HtmlEncode(s);
    private const string Head = "<!doctype html><html lang='pt-BR'><meta charset='utf-8'><title>Dynamic Island · Arquivos</title><style>body{font:15px system-ui;max-width:1100px;margin:48px auto;padding:24px;color:#152521;background:#f5f8f6}h1{font-size:32px}table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:12px;border-bottom:1px solid #cddbd4}img{width:72px;height:60px;object-fit:contain}li{margin:10px 0}code{overflow-wrap:anywhere}</style>";

    private static async Task Index(IReadOnlyList<string> files, string output, CancellationToken token)
    {
        var html = new StringBuilder(Head + "<h1>Índice de arquivos</h1><table><tr><th>Prévia</th><th>Nome / caminho</th><th>Bytes</th><th>Modificado</th></tr>");
        foreach (string file in files)
        {
            token.ThrowIfCancellationRequested(); var info = new FileInfo(file); string thumb = "";
            if (FileActionCatalog.IsImage(file))
            {
                try
                {
                    thumb = "<img alt='' src='data:image/jpeg;base64," + Convert.ToBase64String(FileImageOperations.Thumbnail(file)) + "'>";
                }
                catch (ImageMagick.MagickException) { thumb = "Sem prévia"; }
                catch (InvalidDataException) { thumb = "Sem prévia"; }
            }
            html.Append($"<tr><td>{thumb}</td><td>{Html(info.Name)}<br><small>{Html(info.FullName)}</small></td><td>{info.Length}</td><td>{info.LastWriteTime:yyyy-MM-dd HH:mm}</td></tr>");
        }
        await File.WriteAllTextAsync(Path.Combine(output, "indice.html"), html + "</table></html>", token);
    }

    private static async Task DuplicateReport(IReadOnlyList<string> files, string output, FileActionOptions options, IProgress<string>? progress, CancellationToken token)
    {
        var hashes = new Dictionary<string, List<string>>();
        int count = 0;
        foreach (var sameSize in files.GroupBy(p => new FileInfo(p).Length).Where(g => g.Count() > 1))
            foreach (string file in sameSize)
            {
                token.ThrowIfCancellationRequested(); progress?.Report($"Comparando conteúdo · {++count} · {Path.GetFileName(file)}");
                await using var stream = File.OpenRead(file);
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (!hashes.TryGetValue(hash, out var group)) hashes[hash] = group = [];
                group.Add(file);
            }
        var html = new StringBuilder(Head + "<h1>Duplicados exatos</h1><p>Igualdade por SHA-256. Nenhum arquivo foi apagado.</p>");
        int groups = 0;
        foreach (var group in hashes.Values.Where(g => g.Count > 1))
        {
            html.Append($"<h2>Grupo {++groups}</h2><ul>");
            foreach (string path in group) html.Append("<li><code>" + Html(path) + "</code></li>");
            html.Append("</ul>");
        }
        if (groups == 0) html.Append("<p>Nenhuma cópia exata encontrada.</p>");
        html.Append("<h2>Imagens visualmente parecidas</h2><p>Candidatas por hash visual de 64 pixels; confira antes de decidir.</p>");
        if (options.FindSimilar)
        {
            var imageHashes = new List<(string Path, ulong Hash)>(); int pairs = 0;
            foreach (string file in files.Where(FileActionCatalog.IsImage))
            {
                token.ThrowIfCancellationRequested();
                if (imageHashes.Count >= 1000) { html.Append("<p>Similaridade limitada às primeiras 1.000 imagens.</p>"); break; }
                try
                {
                    progress?.Report("Comparando aparência · " + Path.GetFileName(file));
                    ulong hash = FileImageOperations.VisualHash(file);
                    foreach (var other in imageHashes)
                        if (pairs < 5000 && System.Numerics.BitOperations.PopCount(hash ^ other.Hash) <= 6)
                        { html.Append($"<p>{Html(other.Path)}<br>≈ {Html(file)}</p>"); pairs++; }
                    imageHashes.Add((file, hash));
                }
                catch (ImageMagick.MagickException) { html.Append("<p>Não foi possível comparar: " + Html(file) + "</p>"); }
                catch (InvalidDataException) { html.Append("<p>Não foi possível comparar: " + Html(file) + "</p>"); }
            }
            if (pairs == 0) html.Append("<p>Nenhum par visualmente parecido encontrado.</p>");
            if (pairs >= 5000) html.Append("<p>Relatório limitado a 5.000 pares.</p>");
        }
        else html.Append("<p>Comparação visual desativada.</p>");
        await File.WriteAllTextAsync(Path.Combine(output, "duplicados.html"), html + "</html>", token);
    }

    private static string ValidatedFormat(FileAction action, string format)
    {
        string[] allowed = action switch
        {
            FileAction.Images => ["png", "jpg", "webp"],
            FileAction.Video => ["mp4", "mov", "webm", "mp3", "gif", "jpg"],
            _ => ["mp3", "wav", "m4a", "ogg"]
        };
        return allowed.Contains(format) ? format : throw new InvalidDataException("Formato de saída inválido.");
    }

    private static string Number(double n) => n.ToString(CultureInfo.InvariantCulture);
    private static async Task ConvertMedia(FileAction action, string source, string target, FileActionOptions options, CancellationToken token)
    {
        if (action == FileAction.Images)
        {
            try { FileImageOperations.Convert(source, target, options, token); }
            catch { if (File.Exists(target)) File.Delete(target); throw; }
            return;
        }
        int width = Math.Clamp(options.MaxWidth, 16, 8192), quality = Math.Clamp(options.Quality, 10, 100);
        if (!double.IsFinite(options.StartSeconds) || !double.IsFinite(options.DurationSeconds) || options.StartSeconds < 0 || options.DurationSeconds < 0)
            throw new InvalidDataException("Início e duração devem ser números positivos.");
        string format = Path.GetExtension(target).TrimStart('.');
        List<string> args = ["-v", "error", "-nostdin", "-n"];
        if (options.StartSeconds > 0) args.AddRange(["-ss", Number(options.StartSeconds)]);
        args.AddRange(["-i", Path.GetFullPath(source)]);
        if (options.DurationSeconds > 0) args.AddRange(["-t", Number(options.DurationSeconds)]);
        if (action == FileAction.Images || format == "jpg")
        {
            args.AddRange(["-frames:v", "1", "-vf", $"scale=w='min({width},iw)':h=-2"]);
            if (format == "jpg") args.AddRange(["-q:v", Number(2 + (100 - quality) / 4)]);
            if (format == "webp") args.AddRange(["-quality", Number(quality)]);
            if (format == "png") args.AddRange(["-compression_level", "9"]);
        }
        else if (format is "mp3" or "wav" or "m4a" or "ogg")
        {
            args.Add("-vn");
            if (format != "wav") args.AddRange(["-b:a", quality >= 85 ? "256k" : quality >= 55 ? "160k" : "96k"]);
            args.AddRange(format switch { "m4a" => ["-c:a", "aac"], "ogg" => ["-c:a", "libvorbis"], "mp3" => ["-c:a", "libmp3lame"], _ => new[] { "-c:a", "pcm_s16le" } });
        }
        else if (format == "gif") args.AddRange(["-an", "-vf", $"fps=12,scale={Math.Min(width, 640)}:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse"]);
        else
        {
            args.AddRange(["-vf", $"scale=w='min({width},iw)':h=-2", "-c:v", format == "webm" ? "libvpx-vp9" : "libx264", "-crf", Number(18 + (100 - quality) / 4), "-b:v", "0"]);
            if (format != "webm") { args.RemoveRange(args.Count - 2, 2); args.AddRange(["-preset", "medium", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-c:a", "aac"]); }
            else args.AddRange(["-c:a", "libopus"]);
        }
        args.Add(target);
        try { await FileTools.RunAsync(FileTools.Find("ffmpeg")!, args, token); }
        catch { if (File.Exists(target)) File.Delete(target); throw; }
        if (!File.Exists(target) || new FileInfo(target).Length == 0) throw new IOException("O conversor não gerou um arquivo válido.");
    }

    private static async Task ImagePdf(IReadOnlyList<string> files, string output, bool contactSheet, FileActionOptions options, IProgress<string>? progress, CancellationToken token)
    {
        using var doc = new PdfDocument(); PdfPage? page = null; XGraphics? graphics = null; int index = 0;
        try
        {
            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested(); progress?.Report($"{index + 1}/{files.Count} · {Path.GetFileName(file)}");
                if (!contactSheet || index % 12 == 0)
                {
                    graphics?.Dispose(); page = doc.AddPage(); graphics = XGraphics.FromPdfPage(page);
                }
                string temp = Path.Combine(output, ".image-" + Guid.NewGuid().ToString("N") + ".jpg");
                try
                {
                    await ConvertMedia(FileAction.Images, file, temp, options with { Format = "jpg", MaxWidth = contactSheet ? 640 : options.MaxWidth }, token);
                    using var image = XImage.FromFile(temp);
                    double cellW = (page!.Width.Point - 48) / (contactSheet ? 3 : 1);
                    double cellH = (page.Height.Point - 48) / (contactSheet ? 4 : 1);
                    int cell = contactSheet ? index % 12 : 0;
                    double scale = Math.Min((cellW - 12) / image.PixelWidth, (cellH - 12) / image.PixelHeight);
                    double w = image.PixelWidth * scale, h = image.PixelHeight * scale;
                    graphics!.DrawImage(image, 24 + (cell % 3) * cellW + (cellW - w) / 2,
                        24 + (cell / 3) * cellH + (cellH - h) / 2, w, h);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                index++;
            }
            graphics?.Dispose(); graphics = null;
            doc.Save(Path.Combine(output, contactSheet ? "folha-de-contato.pdf" : "imagens.pdf"));
        }
        finally { graphics?.Dispose(); }
    }
}
