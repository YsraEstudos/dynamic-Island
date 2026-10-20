namespace Island.Core.Files;

public enum FileAction
{
    Rename, Duplicates, Package, Index, ContactSheet, Images, ImagesPdf,
    OfficePdf, MergePdf, SplitPdf, CompressPdf, Video, Audio, Extract, CsvToXlsx, JsonToCsv
}

public sealed record FileActionOptions
{
    public string Name { get; init; } = "pacote";
    public string RenamePattern { get; init; } = "{name}_{n:000}";
    public string Format { get; init; } = "jpg";
    public int MaxWidth { get; init; } = 1920;
    public int Quality { get; init; } = 80;
    public double StartSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public bool FindSimilar { get; init; } = true;
}

public static class FileActionCatalog
{
    public static bool IsImage(string path) => Has(path, ".png", ".jpg", ".jpeg", ".webp", ".heic", ".heif", ".bmp");
    public static bool IsVideo(string path) => Has(path, ".mp4", ".mov", ".webm", ".mkv", ".avi");
    public static bool IsAudio(string path) => Has(path, ".mp3", ".wav", ".m4a", ".ogg", ".flac", ".aac");
    public static bool Has(string path, params string[] extensions) => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<FileAction> For(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return [];
        List<FileAction> actions = [FileAction.Rename, FileAction.Duplicates, FileAction.Package, FileAction.Index];
        if (paths.All(IsImage)) actions.AddRange([FileAction.ContactSheet, FileAction.Images, FileAction.ImagesPdf]);
        if (paths.All(IsVideo)) actions.Add(FileAction.Video);
        if (paths.All(IsAudio)) actions.Add(FileAction.Audio);
        if (paths.All(p => Has(p, ".docx", ".pptx", ".xlsx"))) actions.Add(FileAction.OfficePdf);
        if (paths.All(p => Has(p, ".pdf"))) actions.AddRange([FileAction.MergePdf, FileAction.SplitPdf, FileAction.CompressPdf]);
        if (paths.All(p => Has(p, ".zip"))) actions.Add(FileAction.Extract);
        if (paths.All(p => Has(p, ".csv"))) actions.Add(FileAction.CsvToXlsx);
        if (paths.All(p => Has(p, ".json"))) actions.Add(FileAction.JsonToCsv);
        return actions;
    }
    public static string Label(FileAction action) => action switch
    {
        FileAction.Rename => "Renomear em lote", FileAction.Duplicates => "Encontrar duplicados",
        FileAction.Package => "Criar pacote ZIP", FileAction.Index => "Montar índice",
        FileAction.ContactSheet => "Folha de contato", FileAction.Images => "Converter imagens",
        FileAction.ImagesPdf => "Imagens em PDF", FileAction.OfficePdf => "Documentos em PDF",
        FileAction.MergePdf => "Juntar PDFs", FileAction.SplitPdf => "Dividir PDFs",
        FileAction.CompressPdf => "Comprimir PDFs", FileAction.Video => "Vídeo e prévias",
        FileAction.Audio => "Áudio e cortes", FileAction.Extract => "Extrair ZIP",
        FileAction.CsvToXlsx => "CSV para XLSX", _ => "JSON para CSV"
    };
}
