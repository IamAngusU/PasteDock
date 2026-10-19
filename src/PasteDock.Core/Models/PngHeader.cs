using System.Buffers.Binary;

namespace PasteDock.Core.Models;

public static class PngHeader
{
    public static (int Width, int Height) ReadDimensions(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature)
            || BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]) != 13
            || !bytes[12..16].SequenceEqual("IHDR"u8))
            throw new InvalidDataException("The image does not contain a valid PNG header.");
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]);
        if (width <= 0 || height <= 0 || (long)width * height > Limits.MaxImagePixels)
            throw new InvalidDataException("Unsupported image dimensions.");
        return (width, height);
    }
}
