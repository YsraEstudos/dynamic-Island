using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Island.Core.Capture;

namespace Island.Core.Tests.Capture;

public sealed class PngEncoderTests
{
    [Fact]
    public void Encodes_a_2x2_image_as_a_valid_png_with_the_right_pixels()
    {
        // BGRA, top-down: red, green / blue, white. The alpha byte is ignored by the encoder.
        byte[] bgra =
        [
            0x00, 0x00, 0xFF, 0xFF,  0x00, 0xFF, 0x00, 0x00,
            0xFF, 0x00, 0x00, 0xFF,  0xFF, 0xFF, 0xFF, 0x80,
        ];

        byte[] png = PngEncoder.Encode(2, 2, bgra);

        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], png[..8]);
        var chunks = ReadChunks(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(c => c.Type));

        byte[] header = chunks[0].Data;
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4)));
        Assert.Equal(8, header[8]);  // bit depth
        Assert.Equal(2, header[9]);  // RGB

        byte[] raw = Inflate(chunks[1].Data);
        byte[] expected =
        [
            0, 255, 0, 0,   0, 255, 0,
            0, 0, 0, 255,   255, 255, 255,
        ];
        Assert.Equal(expected, raw);
    }

    [Fact]
    public void IEND_carries_the_standard_CRC()
    {
        byte[] png = PngEncoder.Encode(1, 1, [0, 0, 0, 0]);

        var last = ReadChunks(png)[^1];

        Assert.Equal("IEND", last.Type);
        Assert.Equal(0xAE426082u, last.Crc);
    }

    [Fact]
    public void Rejects_a_buffer_that_does_not_match_the_size()
    {
        Assert.Throws<ArgumentException>(() => PngEncoder.Encode(2, 2, new byte[15]));
    }

    private static byte[] Inflate(byte[] zlib)
    {
        using var input = new MemoryStream(zlib);
        using var inflater = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflater.CopyTo(output);
        return output.ToArray();
    }

    private static List<(string Type, byte[] Data, uint Crc)> ReadChunks(byte[] png)
    {
        var chunks = new List<(string, byte[], uint)>();
        int offset = 8;
        while (offset < png.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            byte[] data = png[(offset + 8)..(offset + 8 + (int)length)];
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + (int)length, 4));
            chunks.Add((type, data, crc));
            offset += 12 + (int)length;
        }
        return chunks;
    }
}
