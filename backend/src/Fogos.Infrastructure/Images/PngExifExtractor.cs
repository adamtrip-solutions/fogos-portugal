using System.Buffers.Binary;

namespace Fogos.Infrastructure.Images;

/// <summary>
/// Walks the chunk structure of a PNG looking for the <c>eXIf</c> chunk (a raw TIFF/EXIF block). A direct
/// port of the legacy <c>ImageProcessingTool::findExifChunk</c> PNG walker: read the 8-byte signature,
/// then iterate <c>[len:4][type:4][data:len][crc:4]</c> records, returning the <c>eXIf</c> payload or
/// stopping at <c>IEND</c>. The extracted bytes are handed to ImageSharp's <c>ExifProfile</c> parser
/// rather than hand-parsing TIFF.
/// </summary>
public static class PngExifExtractor
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>True when <paramref name="bytes"/> begins with the 8-byte PNG signature.</summary>
    public static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 && bytes[..8].SequenceEqual(Signature);

    /// <summary>
    /// Finds the <c>eXIf</c> chunk and copies its payload to <paramref name="exif"/>. Returns false when the
    /// input is not a PNG, is truncated, has no <c>eXIf</c> chunk, or reaches <c>IEND</c> first.
    /// </summary>
    public static bool TryFindExifChunk(ReadOnlySpan<byte> bytes, out byte[] exif)
    {
        exif = [];
        if (!IsPng(bytes))
            return false;

        var offset = 8;
        while (offset + 8 <= bytes.Length)
        {
            var chunkLen = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            var type = bytes.Slice(offset + 4, 4);
            var dataStart = offset + 8;
            var dataEnd = dataStart + (long)chunkLen;

            // A well-formed chunk needs its data plus a 4-byte CRC within bounds.
            if (dataEnd + 4 > bytes.Length)
                return false;

            if (type.SequenceEqual("eXIf"u8))
            {
                exif = bytes.Slice(dataStart, (int)chunkLen).ToArray();
                return exif.Length > 0;
            }

            if (type.SequenceEqual("IEND"u8))
                return false;

            offset = (int)dataEnd + 4;
        }

        return false;
    }
}
