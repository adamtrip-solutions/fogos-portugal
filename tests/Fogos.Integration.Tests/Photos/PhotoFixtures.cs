using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Fogos.Integration.Tests.Photos;

/// <summary>
/// Generates in-memory image fixtures: JPEG/PNG with (or without) GPS EXIF, plus a hand-crafted PNG
/// whose <c>eXIf</c> chunk is spliced in manually so the chunk walker is exercised independently of
/// ImageSharp's own PNG writer.
/// </summary>
public static class PhotoFixtures
{
    public const double Lat = 40.5041666667;   // 40° 30' 15" N
    public const double Lng = -8.1666666667;   // 8° 10' 0" W

    /// <summary>EXIF profile with GPS (N/W refs), altitude, heading, and DateTimeOriginal.</summary>
    public static ExifProfile GpsProfile(
        string latRef = "N", string lngRef = "W",
        string? dateTimeOriginal = "2026:07:15 14:30:00")
    {
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.GPSLatitudeRef, latRef);
        profile.SetValue(ExifTag.GPSLatitude, [new Rational(40u, 1u), new Rational(30u, 1u), new Rational(15u, 1u)]);
        profile.SetValue(ExifTag.GPSLongitudeRef, lngRef);
        profile.SetValue(ExifTag.GPSLongitude, [new Rational(8u, 1u), new Rational(10u, 1u), new Rational(0u, 1u)]);
        profile.SetValue(ExifTag.GPSAltitude, new Rational(123u, 1u));
        profile.SetValue(ExifTag.GPSImgDirection, new Rational(180u, 1u));
        if (dateTimeOriginal is not null)
            profile.SetValue(ExifTag.DateTimeOriginal, dateTimeOriginal);
        return profile;
    }

    /// <summary>JPEG with the given EXIF profile (or none). Pixels vary with <paramref name="seed"/> so signatures differ.</summary>
    public static byte[] Jpeg(ExifProfile? exif, int width = 320, int height = 240, byte seed = 100)
    {
        using var image = NewImage(width, height, seed);
        image.Metadata.ExifProfile = exif;
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = 90 });
        return ms.ToArray();
    }

    /// <summary>PNG carrying an <c>eXIf</c> chunk written by ImageSharp's encoder.</summary>
    public static byte[] Png(ExifProfile? exif, int width = 320, int height = 240, byte seed = 100)
    {
        using var image = NewImage(width, height, seed);
        image.Metadata.ExifProfile = exif;
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// A PNG whose <c>eXIf</c> chunk is spliced in by hand right after IHDR (raw TIFF payload,
    /// correct length + CRC), independent of any encoder's metadata support.
    /// </summary>
    public static byte[] PngWithSplicedExifChunk(byte[] exifTiffBytes, int width = 8, int height = 8)
    {
        var plain = Png(exif: null, width, height);

        // IHDR is always the first chunk: signature(8) + [len 4 | "IHDR" 4 | data len | crc 4].
        var ihdrLen = BinaryPrimitives.ReadUInt32BigEndian(plain.AsSpan(8, 4));
        var spliceAt = 8 + 8 + (int)ihdrLen + 4;

        using var ms = new MemoryStream();
        ms.Write(plain, 0, spliceAt);
        WriteChunk(ms, "eXIf", exifTiffBytes);
        ms.Write(plain, spliceAt, plain.Length - spliceAt);
        return ms.ToArray();
    }

    /// <summary>Raw TIFF/EXIF bytes for a GPS profile (what a real eXIf chunk contains).</summary>
    public static byte[] ExifTiffBytes(ExifProfile? profile = null) =>
        (profile ?? GpsProfile()).ToByteArray() ?? throw new InvalidOperationException("empty exif profile");

    private static Image<Rgba32> NewImage(int width, int height, byte seed)
    {
        var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            image[x, y] = new Rgba32((byte)((x + seed) % 256), (byte)((y + seed * 3) % 256), seed, 255);
        return image;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        stream.Write(len);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        Span<byte> crc = stackalloc byte[4];
        var crc32 = new System.IO.Hashing.Crc32();
        crc32.Append(typeBytes);
        crc32.Append(data);
        BinaryPrimitives.WriteUInt32BigEndian(crc, crc32.GetCurrentHashAsUInt32());
        stream.Write(crc);
    }
}
