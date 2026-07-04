using Fogos.Domain.Geo;

namespace Fogos.Infrastructure.Images;

/// <summary>
/// Result of <see cref="ImageProcessor.ProcessAsync"/>: the re-encoded (metadata-stripped) JPEG plus the
/// GPS/EXIF facts salvaged from the original upload. GPS is mandatory — the processor throws
/// <see cref="MissingGpsException"/> rather than returning a photo without it.
/// </summary>
public sealed record ProcessedPhoto
{
    /// <summary>Re-encoded JPEG bytes (baseline, quality 82, longest edge ≤ 2560, all metadata stripped).</summary>
    public required byte[] JpegBytes { get; init; }

    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>Decimal-degree GPS parsed from EXIF (DMS + N/S/E/W refs).</summary>
    public required GeoPoint Gps { get; init; }

    /// <summary>Metres above sea level (negative below), when present.</summary>
    public double? Altitude { get; init; }

    /// <summary>Compass heading in degrees (GPSImgDirection), when present.</summary>
    public double? Heading { get; init; }

    /// <summary>DateTimeOriginal, interpreted Europe/Lisbon → UTC, when present and parseable.</summary>
    public DateTimeOffset? TakenAt { get; init; }

    /// <summary>SHA-256 (lowercase hex) of the ORIGINAL upload bytes — the dedup key.</summary>
    public required string Signature { get; init; }
}
