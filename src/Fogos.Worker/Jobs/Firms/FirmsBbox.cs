using System.Globalization;
using Fogos.Domain.Geo;

namespace Fogos.Worker.Jobs.Firms;

/// <summary>
/// FIRMS area bounding-box math: a ±0.10° square (~11 km) around an incident, formatted as the
/// <c>west,south,east,north</c> string the FIRMS area API expects (6-decimal rounding, invariant).
/// </summary>
public static class FirmsBbox
{
    /// <summary>Bounding-box half-size in decimal degrees (legacy <c>BBOX_DELTA</c>).</summary>
    public const double Delta = 0.10;

    public static string Around(GeoPoint point) => Around(point.Latitude, point.Longitude);

    public static string Around(double lat, double lng)
    {
        var west = Round(lng - Delta);
        var south = Round(lat - Delta);
        var east = Round(lng + Delta);
        var north = Round(lat + Delta);
        return string.Create(CultureInfo.InvariantCulture, $"{west},{south},{east},{north}");
    }

    private static double Round(double v) => Math.Round(v, 6, MidpointRounding.AwayFromZero);
}
