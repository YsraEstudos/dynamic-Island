using ImageMagick;

namespace Island.Core.Files;

internal static class FileImageOperations
{
    private static MagickFormat Format(string extension) => extension.ToLowerInvariant().TrimStart('.') switch
    {
        "jpg" or "jpeg" => MagickFormat.Jpeg, "png" => MagickFormat.Png,
        "webp" => MagickFormat.WebP, "heic" or "heif" => MagickFormat.Heic,
        "bmp" => MagickFormat.Bmp, _ => throw new InvalidDataException("Formato de imagem não suportado.")
    };

    private static MagickImage Read(string path)
    {
        using var stream = File.OpenRead(path);
        var settings = new MagickReadSettings { Format = Format(Path.GetExtension(path)), FrameCount = 1 };
        var info = new MagickImageInfo(stream, settings);
        if ((long)info.Width * info.Height > 100_000_000) throw new InvalidDataException("Imagem excede 100 megapixels. Reduza a imagem antes de processar.");
        stream.Position = 0;
        var image = new MagickImage(stream, settings);
        image.AutoOrient();
        return image;
    }

    public static void Convert(string source, string target, FileActionOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var image = Read(source);
        image.Resize(new MagickGeometry((uint)Math.Clamp(options.MaxWidth, 16, 8192), 0) { Greater = true });
        image.Quality = (uint)Math.Clamp(options.Quality, 10, 100);
        image.Strip();
        var format = Format(Path.GetExtension(target));
        if (format == MagickFormat.Jpeg) { image.BackgroundColor = MagickColors.White; image.Alpha(AlphaAction.Remove); }
        token.ThrowIfCancellationRequested();
        image.Write(target, format);
    }

    public static byte[] Thumbnail(string source)
    {
        using var image = Read(source);
        image.Thumbnail(160, 120);
        image.BackgroundColor = MagickColors.White; image.Alpha(AlphaAction.Remove);
        image.Strip(); image.Quality = 75;
        return image.ToByteArray(MagickFormat.Jpeg);
    }

    public static ulong VisualHash(string source)
    {
        using var image = Read(source);
        image.ColorSpace = ColorSpace.Gray;
        image.Resize(new MagickGeometry(8, 8) { IgnoreAspectRatio = true });
        image.Depth = 8;
        byte[] bytes = image.ToByteArray(MagickFormat.Gray);
        if (bytes.Length != 64) throw new InvalidDataException("Não foi possível comparar a imagem.");
        double mean = bytes.Average(b => (double)b); ulong hash = 0;
        for (int i = 0; i < 64; i++) if (bytes[i] > mean) hash |= 1UL << i;
        return hash;
    }
}
