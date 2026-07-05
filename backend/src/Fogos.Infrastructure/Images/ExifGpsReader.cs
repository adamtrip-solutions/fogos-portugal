using System.Globalization;
using Fogos.Domain.Geo;
using Fogos.Domain.Time;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace Fogos.Infrastructure.Images;

/// <summary>GPS/EXIF facts salvaged from an <see cref="ExifProfile"/> (null when GPS is absent).</summary>
public readonly record struct ExifGps(GeoPoint Point, double? Altitude, double? Heading, DateTimeOffset? TakenAt);

/// <summary>
/// Reads GPS coordinates (DMS → decimal, honouring N/S/E/W refs), altitude (with the below-sea-level ref),
/// heading (GPSImgDirection), and DateTimeOriginal from an EXIF profile. Ported from
/// <c>ImageProcessingTool::readGpsCoord</c> / <c>extractPngExif</c>. DateTimeOriginal is parsed
/// <c>yyyy:MM:dd HH:mm:ss</c>, interpreted Europe/Lisbon, and converted to UTC via the clock.
/// </summary>
public static class ExifGpsReader
{
    public static ExifGps? Read(ExifProfile? profile, IClock clock)
    {
        if (profile is null)
            return null;

        var lat = ReadCoordinate(profile, ExifTag.GPSLatitude, ExifTag.GPSLatitudeRef, "S");
        var lng = ReadCoordinate(profile, ExifTag.GPSLongitude, ExifTag.GPSLongitudeRef, "W");
        if (lat is null || lng is null)
            return null;

        // GeoPoint validates ranges; a corrupt profile that yields out-of-range values is treated as "no GPS".
        GeoPoint point;
        try
        {
            point = GeoPoint.FromLatLng(lat.Value, lng.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        return new ExifGps(point, ReadAltitude(profile), ReadRational(profile, ExifTag.GPSImgDirection), ReadTakenAt(profile, clock));
    }

    private static double? ReadCoordinate(ExifProfile profile, ExifTag<Rational[]> tag, ExifTag<string> refTag, string negativeRef)
    {
        if (!profile.TryGetValue(tag, out var value) || value?.Value is not { Length: >= 3 } dms)
            return null;

        var deg = ToDouble(dms[0]);
        var min = ToDouble(dms[1]);
        var sec = ToDouble(dms[2]);
        if (deg is null || min is null || sec is null)
            return null;

        var result = deg.Value + (min.Value / 60) + (sec.Value / 3600);

        if (profile.TryGetValue(refTag, out var refValue)
            && string.Equals(refValue?.Value?.Trim(), negativeRef, StringComparison.OrdinalIgnoreCase))
            result = -result;

        return result;
    }

    private static double? ReadAltitude(ExifProfile profile)
    {
        var altitude = ReadRational(profile, ExifTag.GPSAltitude);
        if (altitude is null)
            return null;

        // GPSAltitudeRef == 1 means below sea level.
        if (profile.TryGetValue(ExifTag.GPSAltitudeRef, out var refValue) && refValue?.Value == 1)
            altitude = -altitude;

        return altitude;
    }

    private static double? ReadRational(ExifProfile profile, ExifTag<Rational> tag) =>
        profile.TryGetValue(tag, out var value) ? ToDouble(value?.Value) : null;

    private static DateTimeOffset? ReadTakenAt(ExifProfile profile, IClock clock)
    {
        if (!profile.TryGetValue(ExifTag.DateTimeOriginal, out var value) || string.IsNullOrWhiteSpace(value?.Value))
            return null;

        if (!DateTime.TryParseExact(value.Value, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var naive))
            return null;

        return clock.FromLisbon(naive).ToUniversalTime();
    }

    private static double? ToDouble(Rational? rational)
    {
        if (rational is not { } r || r.Denominator == 0)
            return null;
        return (double)r.Numerator / r.Denominator;
    }
}
