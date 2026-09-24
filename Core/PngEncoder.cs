using System.Buffers.Binary;
using System.IO.Compression;

namespace SentinelRelay.Core;

public static class PngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] EncodeBgra(ReadOnlySpan<byte> pixels, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "PNG dimensions must be positive.");
        var pixelBytes = checked(width * height * 4);
        if (pixels.Length < pixelBytes)
            throw new ArgumentException("The BGRA buffer is smaller than the requested image.", nameof(pixels));

        var scanlineLength = checked(width * 4 + 1);
        var raw = new byte[checked(scanlineLength * height)];
        for (var y = 0; y < height; y++)
        {
            var destination = y * scanlineLength;
            raw[destination++] = 0; // PNG filter: None
            var source = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                raw[destination++] = pixels[source + 2];
                raw[destination++] = pixels[source + 1];
                raw[destination++] = pixels[source];
                raw[destination++] = 255;
                source += 4;
            }
        }

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
                zlib.Write(raw);
            compressed = compressedStream.ToArray();
        }

        using var output = new MemoryStream(Signature.Length + compressed.Length + 128);
        output.Write(Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(header.Slice(4, 4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, compressed);
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(integer, data.Length);
        output.Write(integer);
        output.Write(type);
        output.Write(data);

        var crc = ComputeCrc(type, data);
        BinaryPrimitives.WriteUInt32BigEndian(integer, crc);
        output.Write(integer);
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type)
            crc = CrcTable[(byte)(crc ^ value)] ^ (crc >> 8);
        foreach (var value in data)
            crc = CrcTable[(byte)(crc ^ value)] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }
}
