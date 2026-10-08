using System.Buffers.Binary;
using Island.Windows.Clipboard;

namespace Island.Windows.Tests;

public sealed class ClipboardImageConverterTests
{
    [Fact]
    public void DibToBmp_TwoByTwo24Bit_WrapsWithCorrectHeaderFields()
    {
        // 2x2, 24 bpp: each row is 2*3 = 6 bytes, padded to 8. Pixel data = 16 bytes. DIB = 40 + 16 = 56 bytes.
        byte[] dib = Build2x2Dib24();
        byte[] pixels = dib[40..];

        byte[]? bmp = ClipboardImageConverter.DibToBmp(dib);

        Assert.NotNull(bmp);
        Assert.Equal((byte)'B', bmp![0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(14 + dib.Length, bmp.Length);                                            // 70
        Assert.Equal((uint)(14 + dib.Length), BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(2)));   // bfSize
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(6)));             // reserved
        Assert.Equal(54u, BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(10)));           // bfOffBits = 14 + 40
        Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(14)));             // biSize, copied intact
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(18)));              // biWidth
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(22)));              // biHeight
        Assert.Equal(24, BinaryPrimitives.ReadUInt16LittleEndian(bmp.AsSpan(28)));            // biBitCount
        Assert.Equal(pixels, bmp[54..]);                                                      // pixel data at bfOffBits
    }

    [Fact]
    public void DibToBmp_Bitfields32Bit_OffsetIncludesThreeMasks()
    {
        // 1x1, 32 bpp, BI_BITFIELDS with the 40-byte header: 12 bytes of masks follow the header.
        // DIB = 40 + 12 + 4 = 56 bytes, so bfOffBits = 14 + 40 + 12 = 66.
        var dib = new byte[56];
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 3);   // BI_BITFIELDS
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(40), 0x00FF0000);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(44), 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(48), 0x000000FF);
        dib[52] = 1; dib[53] = 2; dib[54] = 3; dib[55] = 0xFF;

        byte[]? bmp = ClipboardImageConverter.DibToBmp(dib);

        Assert.NotNull(bmp);
        Assert.Equal(70, bmp!.Length);
        Assert.Equal(70u, BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(2)));
        Assert.Equal(66u, BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(10)));
        Assert.Equal(new byte[] { 1, 2, 3, 0xFF }, bmp[66..]);
    }

    [Fact]
    public void DibToBmp_EightBitWithoutClrUsed_CountsFullPalette()
    {
        // 1x1, 8 bpp, clrUsed = 0 means 256 palette entries (1024 bytes). Row = 4 bytes (padded).
        // DIB = 40 + 1024 + 4 = 1068 bytes, bfOffBits = 14 + 40 + 1024 = 1078.
        var dib = new byte[40 + 1024 + 4];
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 8);

        byte[]? bmp = ClipboardImageConverter.DibToBmp(dib);

        Assert.NotNull(bmp);
        Assert.Equal(14u + 1068u, BinaryPrimitives.ReadUInt32LittleEndian(bmp!.AsSpan(2)));
        Assert.Equal(1078u, BinaryPrimitives.ReadUInt32LittleEndian(bmp.AsSpan(10)));
    }

    [Fact]
    public void DibToBmp_HeaderSmallerThanInfoHeader_ReturnsNull()
    {
        var dib = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), 12);   // BITMAPCOREHEADER, unsupported

        Assert.Null(ClipboardImageConverter.DibToBmp(dib));
    }

    [Fact]
    public void DibToBmp_TruncatedData_ReturnsNull()
    {
        Assert.Null(ClipboardImageConverter.DibToBmp(new byte[10]));
        Assert.Null(ClipboardImageConverter.DibToBmp(null!));
    }

    [Fact]
    public void DibToBmp_JpegCompression_ReturnsNull()
    {
        var dib = new byte[40];
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), 40);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 4);   // BI_JPEG

        Assert.Null(ClipboardImageConverter.DibToBmp(dib));
    }

    /// <summary>Hand-built 2x2, 24 bpp, bottom-up DIB: BITMAPINFOHEADER + 16 bytes of padded rows.</summary>
    private static byte[] Build2x2Dib24()
    {
        var dib = new byte[40 + 16];
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0), 40);        // biSize
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 2);         // biWidth
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 2);         // biHeight
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);       // biPlanes
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 24);      // biBitCount
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), 0);       // biCompression = BI_RGB
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(20), 16);      // biSizeImage
        for (int i = 0; i < 16; i++) dib[40 + i] = (byte)(i + 1);          // distinct, recognisable pixels
        return dib;
    }
}
