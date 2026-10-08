using System.IO;
using System.Windows.Media.Imaging;

namespace Island.App.Widgets;

/// <summary>
/// Keeps clipboard images small in memory. Captures arrive as uncompressed BMP (a 1440p screenshot is about 15 MB and the
/// history holds up to 50 items), so the history stores them as lossless PNG and expands one back to BMP only when it is
/// copied again, since CF_DIB is the form every app can paste. Any failure returns the input unchanged.
/// </summary>
public static class ClipboardImageCodec
{
    /// <summary>PNG bytes for a BMP; the input itself when it is not a BMP, cannot be decoded, or PNG is not smaller.</summary>
    public static byte[] Compress(byte[] bytes)
    {
        if (!IsBmp(bytes)) return bytes;

        try
        {
            byte[] png = Encode(new PngBitmapEncoder(), bytes);
            return png.Length < bytes.Length ? png : bytes;
        }
        catch (Exception)
        {
            return bytes;
        }
    }

    /// <summary>BMP bytes for a PNG; the input itself when it is not a PNG or cannot be decoded.</summary>
    public static byte[] ToBmp(byte[] bytes)
    {
        if (!IsPng(bytes)) return bytes;

        try
        {
            return Encode(new BmpBitmapEncoder(), bytes);
        }
        catch (Exception)
        {
            return bytes;
        }
    }

    private static byte[] Encode(BitmapEncoder encoder, byte[] source)
    {
        using var input = new MemoryStream(source);
        BitmapDecoder decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        encoder.Frames.Add(decoder.Frames[0]);
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static bool IsBmp(byte[] bytes) => bytes.Length > 54 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M';

    private static bool IsPng(byte[] bytes) =>
        bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G';
}
