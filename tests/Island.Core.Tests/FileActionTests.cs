using System.IO.Compression;
using Island.Core.Files;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Island.Core.Tests;

public sealed class FileActionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orbit-tests-" + Guid.NewGuid().ToString("N"));
    public FileActionTests() => Directory.CreateDirectory(_root);
    private string File(string name, string text)
    {
        string path = Path.Combine(_root, name);
        System.IO.File.WriteAllText(path, text);
        return path;
    }
    [Fact]
    public void Mixed_inputs_never_offer_lossy_partial_batch_conversions()
    {
        var actions = FileActionCatalog.For(["photo.png", "notes.txt"]);
        Assert.Contains(FileAction.Package, actions);
        Assert.DoesNotContain(FileAction.Images, actions);
        Assert.DoesNotContain(FileAction.Audio, actions);
    }
    [Fact]
    public async Task Rename_copies_preserve_originals_and_make_colliding_names_unique()
    {
        string a = File("a.txt", "same title"), b = File("b.txt", "same title");
        string output = Path.Combine(_root, "out");
        await new FileActionService().RunAsync(FileAction.Rename, [a, b], output,
            new FileActionOptions { RenamePattern = "{content}" });
        Assert.Equal("same title", System.IO.File.ReadAllText(a));
        Assert.Equal(2, Directory.GetFiles(output).Length);
        Assert.True(System.IO.File.Exists(Path.Combine(output, "same title.txt")));
        Assert.True(System.IO.File.Exists(Path.Combine(output, "same title (2).txt")));
    }
    [Fact]
    public async Task Duplicate_report_proves_equal_bytes_and_does_not_delete_files()
    {
        var a = File("a.txt", "identical");
        var b = File("b.txt", "identical");
        var c = File("c.txt", "different");
        string output = Path.Combine(_root, "out");
        await new FileActionService().RunAsync(FileAction.Duplicates, [a, b, c], output, new());
        string report = System.IO.File.ReadAllText(Path.Combine(output, "duplicados.html"));
        Assert.Contains("a.txt", report);
        Assert.Contains("b.txt", report);
        Assert.DoesNotContain("c.txt</", report);
        Assert.True(System.IO.File.Exists(b));
    }
    [Fact]
    public async Task Extraction_rejects_parent_traversal()
    {
        string zip = Path.Combine(_root, "bad.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open())) writer.Write("bad");
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileActionService().RunAsync(
            FileAction.Extract, [zip], Path.Combine(_root, "out"), new()));
        Assert.False(System.IO.File.Exists(Path.Combine(_root, "escape.txt")));
    }
    [Fact]
    public async Task Csv_to_xlsx_keeps_quotes_newlines_unicode_and_formula_text()
    {
        string csv = File("data.csv", "name,value\r\n\"João, A\",\"line1\nline2\"\r\n=1+1,\"a\"\"b\"");
        string output = Path.Combine(_root, "out");
        await new FileActionService().RunAsync(FileAction.CsvToXlsx, [csv], output, new());
        using var zip = ZipFile.OpenRead(Path.Combine(output, "data.xlsx"));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        string xml = reader.ReadToEnd();
        Assert.Contains("João, A", xml);
        Assert.Contains(System.Xml.Linq.XDocument.Parse(xml).Descendants(), e => e.Name.LocalName == "t" && e.Value == "line1\nline2");
        Assert.Contains("=1+1", xml);
        Assert.DoesNotContain("<f>", xml);
    }
    [Fact]
    public async Task Json_to_csv_unions_keys_and_escapes_cells()
    {
        string json = File("data.json", "[{\"a\":\"x,y\"},{\"b\":2}]");
        string output = Path.Combine(_root, "out");
        await new FileActionService().RunAsync(FileAction.JsonToCsv, [json], output, new());
        string csv = System.IO.File.ReadAllText(Path.Combine(output, "data.csv"));
        Assert.Contains("\"a\",\"b\"", csv);
        Assert.Contains("\"x,y\",\"\"", csv);
    }
    [Fact]
    public async Task Merge_and_split_create_readable_pdf_pages_in_input_order()
    {
        string[] inputs = [Path.Combine(_root, "a.pdf"), Path.Combine(_root, "b.pdf")];
        foreach (string path in inputs)
        {
            using var doc = new PdfDocument();
            doc.AddPage(); doc.Save(path);
        }
        string output = Path.Combine(_root, "out");
        var service = new FileActionService();
        await service.RunAsync(FileAction.MergePdf, inputs, output, new());
        string merged = Path.Combine(output, "documentos.pdf");
        using (var doc = PdfReader.Open(merged, PdfDocumentOpenMode.Import)) Assert.Equal(2, doc.PageCount);
        string split = Path.Combine(_root, "split");
        await service.RunAsync(FileAction.SplitPdf, [merged], split, new());
        Assert.Equal(2, Directory.GetFiles(split, "*.pdf").Length);
    }

    [Theory]
    [InlineData(FileAction.Rename)]
    [InlineData(FileAction.Package)]
    public async Task Cancellation_during_a_batch_does_not_expose_incomplete_results(FileAction action)
    {
        string input = File("input.txt", new string('x', 50000));
        string output = Path.Combine(_root, "out");
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelProgress(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileActionService().RunAsync(
            action, [input], output, new(), progress, cancellation.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
        Assert.Equal(50000, new FileInfo(input).Length);
    }

    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<string>
    {
        public void Report(string value) => cancellation.Cancel();
    }
    public void Dispose() => Directory.Delete(_root, true);
}
