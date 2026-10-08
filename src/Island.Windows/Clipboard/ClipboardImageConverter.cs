using System.Buffers.Binary;

namespace Island.Windows.Clipboard;

/// <summary>
/// Converts between packed DIB clipboard data (CF_DIB / CF_DIBV5, no file header) and BMP files.
/// WPF can decode the BMP form, and it is the form SetImage accepts, so the two stay symmetric.
/// </summary>
internal static class ClipboardImageConverter
{
    internal const int BitmapFileHeaderSize = 14;
    private const int BitmapInfoHeaderSize = 40;
    private const uint BI_BITFIELDS = 3;
    private const uint BI_ALPHABITFIELDS = 6;

    /// <summary>
    /// Wraps a packed DIB in a BITMAPFILEHEADER. bfOffBits = 14 + header size + bitfield masks + colour table.
    /// Returns null when the DIB header is malformed or uses a compression this converter does not handle (JPEG/PNG).
    /// </summary>
    public static byte[]? DibToBmp(byte[] dib)
    {
        if (dib is null || dib.Length < BitmapInfoHeaderSize) return null;

        long headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(0));
        // BITMAPCOREHEADER (12 bytes, 3-byte palette) is not supported; everything from BITMAPINFOHEADER up is.
        if (headerSize < BitmapInfoHeaderSize || headerSize > dib.Length) return null;

        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16));
        uint colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(32));

        // BI_RGB, BI_RLE8, BI_RLE4, BI_BITFIELDS, BI_ALPHABITFIELDS are fine. BI_JPEG (4) and BI_PNG (5) payloads
        // and unknown values are rejected rather than wrapped into a BMP that no decoder would read.
        if (compression is 4 or 5 or > 6) return null;

        // A 40-byte header with BI_BITFIELDS/BI_ALPHABITFIELDS has the masks after it. V2+ headers carry them inside.
        long bitfieldBytes = 0;
        if (headerSize == BitmapInfoHeaderSize)
        {
            if (compression == BI_BITFIELDS) bitfieldBytes = 12;
            else if (compression == BI_ALPHABITFIELDS) bitfieldBytes = 16;
        }

        long colorCount = colorsUsed != 0 ? colorsUsed : bitCount <= 8 ? 1L << bitCount : 0;
        long colorTableBytes = colorCount * 4;   // RGBQUAD

        long bfOffBits = BitmapFileHeaderSize + headerSize + bitfieldBytes + colorTableBytes;
        if (bfOffBits > BitmapFileHeaderSize + (long)dib.Length) return null;
        if (bfOffBits > int.MaxValue - 1024) return null;

        var bmp = new byte[BitmapFileHeaderSize + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), (uint)bmp.Length);   // bfSize
        // bytes 6..9: bfReserved1 and bfReserved2, both zero
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), (uint)bfOffBits);
        dib.CopyTo(bmp, BitmapFileHeaderSize);
        return bmp;
    }

    /// <summary>True when the bytes start with a BMP file header and a plausible info header.</summary>
    public static bool IsBmp(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= BitmapFileHeaderSize + BitmapInfoHeaderSize && bytes[0] == (byte)'B' && bytes[1] == (byte)'M';

    /// <summary>Drops the 14-byte BITMAPFILEHEADER so the rest can be placed on the clipboard as CF_DIB.</summary>
    public static byte[] BmpToDib(byte[] bmp) => bmp[BitmapFileHeaderSize..];

    public static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
}
