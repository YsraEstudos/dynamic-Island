using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Island.Core.Capture;

/// <summary>
/// Encodes top-down 32-bit BGRA pixels (stride = width x 4) as an 8-bit RGB PNG. Alpha is dropped because a screen
/// capture is always opaque. Written by hand so the Core project does not depend on WPF or System.Drawing.
/// </summary>
public static class PngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> bgra)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "The image size must be positive.");
        long expected = (long)width * height * 4;
        if (bgra.Length != expected) throw new ArgumentException($"Expected {expected} bytes of BGRA pixels.", nameof(bgra));

        byte[] raw = ToFilteredRgb(width, height, bgra);
        byte[] compressed = Compress(raw);

        using var output = new MemoryStream(compressed.Length + 64);
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
        header[8] = 8;   // bit depth
        header[9] = 2;   // colour type: truecolour RGB
        header[10] = 0;  // compression method (deflate)
        header[11] = 0;  // filter method
        header[12] = 0;  // no interlace
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", compressed);
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    /// <summary>Each row starts with filter type 0 (none), followed by RGB triples.</summary>
    private static byte[] ToFilteredRgb(int width, int height, ReadOnlySpan<byte> bgra)
    {
        int rowBytes = 1 + width * 3;
        var raw = new byte[rowBytes * height];
        for (int y = 0; y < height; y++)
        {
            int dst = y * rowBytes;
            int src = y * width * 4;
            raw[dst] = 0;
            dst++;
            for (int x = 0; x < width; x++, src += 4, dst += 3)
            {
                raw[dst] = bgra[src + 2];      // R
                raw[dst + 1] = bgra[src + 1];  // G
                raw[dst + 2] = bgra[src];      // B
            }
        }
        return raw;
    }

    private static byte[] Compress(byte[] raw)
    {
        using var buffer = new MemoryStream();
        // ZLibStream writes the zlib wrapper (header and Adler-32) that PNG requires.
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw, 0, raw.Length);
        }
        return buffer.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);

        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = Crc32.Update(Crc32.Initial, typeBytes);
        crc = Crc32.Update(crc, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, Crc32.Finish(crc));
        output.Write(crcBytes);
    }

    /// <summary>CRC-32 as PNG defines it (polynomial 0xEDB88320, reflected), computed incrementally.</summary>
    private static class Crc32
    {
        public const uint Initial = 0xFFFFFFFF;

        private static readonly uint[] Table = BuildTable();

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (byte b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        public static uint Finish(uint crc) => crc ^ 0xFFFFFFFF;

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
