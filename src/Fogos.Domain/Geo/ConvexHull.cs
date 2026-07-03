namespace Fogos.Domain.Geo;

/// <summary>
/// Jarvis-march convex hull with a small buffer, in naive degree space — a deliberate
/// verbatim port of the legacy ConvexHullTool used for the FIRMS AOI KML. Portugal-scale
/// areas make the degree-space approximation acceptable.
/// </summary>
public static class ConvexHull
{
    /// <summary>~500 m expressed in degrees, the legacy buffer.</summary>
    public const double DefaultBufferDegrees = 0.0045;

    /// <summary>
    /// Returns the hull vertices (closed ring not required; consumer closes it).
    /// Fewer than 3 distinct points yield an empty result — a hull needs a polygon.
    /// </summary>
    public static IReadOnlyList<GeoPoint> Compute(IReadOnlyCollection<GeoPoint> points, double bufferDegrees = DefaultBufferDegrees)
    {
        var distinct = points.Distinct().ToList();
        if (distinct.Count < 3)
            return [];

        var hull = new List<GeoPoint>();
        var start = distinct.MinBy(p => p.Longitude);
        var current = start;

        do
        {
            hull.Add(current);
            var candidate = distinct[0];
            foreach (var p in distinct.Skip(1))
            {
                if (candidate == current || IsCounterClockwise(current, candidate, p))
                    candidate = p;
            }
            current = candidate;
        } while (current != start && hull.Count <= distinct.Count);

        return bufferDegrees == 0 ? hull : Buffer(hull, bufferDegrees);
    }

    private static bool IsCounterClockwise(GeoPoint a, GeoPoint b, GeoPoint c)
    {
        var cross = (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude)
                    - (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);
        return cross > 0;
    }

    private static List<GeoPoint> Buffer(List<GeoPoint> hull, double degrees)
    {
        var centerLat = hull.Average(p => p.Latitude);
        var centerLng = hull.Average(p => p.Longitude);

        return hull.Select(p =>
        {
            var dLat = p.Latitude - centerLat;
            var dLng = p.Longitude - centerLng;
            var len = Math.Sqrt(dLat * dLat + dLng * dLng);
            if (len == 0)
                return p;
            return GeoPoint.FromLatLng(
                Math.Clamp(p.Latitude + dLat / len * degrees, -90, 90),
                Math.Clamp(p.Longitude + dLng / len * degrees, -180, 180));
        }).ToList();
    }
}
