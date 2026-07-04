using System.Security.Cryptography;
using Fogos.Domain.Time;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace Fogos.Infrastructure.Images;

/// <summary>
/// Port of the legacy <c>ImageProcessingTool</c>. Accepts JPEG (EXIF via image metadata) and PNG (EXIF via
/// the <c>eXIf</c> chunk walker), <b>requires</b> GPS, and re-encodes to a metadata-stripped JPEG:
/// quality 82, longest edge ≤ 2560 (never upscaled). The signature is the SHA-256 of the ORIGINAL upload
/// bytes (the dedup key). Rejections surface as typed exceptions the API maps to 415 / 400 / 422.
/// NOTE: ImageSharp's free (3.1) JPEG encoder is baseline-only — the legacy "progressive" flag has no
/// equivalent, so output is baseline interleaved JPEG (see the migration report).
/// </summary>
public sealed class ImageProcessor(IClock clock)
{
    public const int MaxLongEdge = 2560;
    public const int JpegQuality = 82;

    /// <summary>
    /// Decodes, validates GPS, resizes, strips metadata, and re-encodes the upload. The stream is read in
    /// full up front (the original bytes drive the signature and the PNG walker).
    /// </summary>
    public async Task<ProcessedPhoto> ProcessAsync(Stream input, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        var original = buffer.ToArray();

        var signature = Convert.ToHexStringLower(SHA256.HashData(original));

        var isPng = PngExifExtractor.IsPng(original);
        var isJpeg = IsJpeg(original);
        if (!isPng && !isJpeg)
            throw new UnsupportedImageFormatException();

        using var image = Decode(original);

        // PNG: the ported eXIf walker is the primary path; fall back to whatever the decoder itself parsed.
        var profile = image.Metadata.ExifProfile;
        if (isPng && PngExifExtractor.TryFindExifChunk(original, out var exifBytes))
            profile = SafeProfile(exifBytes) ?? profile;

        var gps = ExifGpsReader.Read(profile, clock)
                  ?? throw new MissingGpsException();

        Resize(image);

        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IccProfile = null;

        using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = JpegQuality, SkipMetadata = true }, ct);

        return new ProcessedPhoto
        {
            JpegBytes = output.ToArray(),
            Width = image.Width,
            Height = image.Height,
            Gps = gps.Point,
            Altitude = gps.Altitude,
            Heading = gps.Heading,
            TakenAt = gps.TakenAt,
            Signature = signature,
        };
    }

    private static Image Decode(byte[] bytes)
    {
        try
        {
            return Image.Load(bytes);
        }
        catch (Exception ex) when (ex is not ImageProcessingException)
        {
            throw new UndecodableImageException();
        }
    }

    private static void Resize(Image image)
    {
        var w = image.Width;
        var h = image.Height;
        if (Math.Max(w, h) <= MaxLongEdge)
            return; // never upscale.

        // Scale the longest edge to the cap, preserving aspect (0 = "auto" for the other dimension).
        var (targetW, targetH) = w >= h ? (MaxLongEdge, 0) : (0, MaxLongEdge);
        image.Mutate(x => x.Resize(targetW, targetH));
    }

    private static ExifProfile? SafeProfile(byte[] exifBytes)
    {
        try
        {
            return new ExifProfile(exifBytes);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
}
