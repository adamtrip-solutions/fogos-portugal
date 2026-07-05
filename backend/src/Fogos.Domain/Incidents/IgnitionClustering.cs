using Fogos.Domain.Geo;

namespace Fogos.Domain.Incidents;

/// <summary>
/// Pure single-linkage spatial clustering of ignition points. Two ignitions link when they lie within
/// the linkage distance; a cluster is a connected component of that link graph (single-linkage: a chain
/// of ignitions each within the distance of the next belongs to one cluster). No I/O — fully testable.
/// </summary>
public static class IgnitionClustering
{
    /// <summary>One ignition to cluster: its incident id and position.</summary>
    public readonly record struct Point(string IncidentId, GeoPoint Position);

    /// <summary>
    /// Groups points into single-linkage clusters using <paramref name="linkKm"/> as the linkage
    /// distance. Returns the connected components (each a list of the points it contains); singletons
    /// included. Order within/among groups is unspecified.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Point>> Group(IReadOnlyList<Point> points, double linkKm)
    {
        var n = points.Count;
        var parent = new int[n];
        for (var i = 0; i < n; i++)
            parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
                parent[ra] = rb;
        }

        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
                if (points[i].Position.DistanceKm(points[j].Position) <= linkKm)
                    Union(i, j);

        var groups = new Dictionary<int, List<Point>>();
        for (var i = 0; i < n; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list))
                groups[root] = list = [];
            list.Add(points[i]);
        }

        return groups.Values.Cast<IReadOnlyList<Point>>().ToList();
    }

    /// <summary>Arithmetic-mean centroid of the points (empty input → throws; callers pass non-empty groups).</summary>
    public static GeoPoint Centroid(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
            throw new ArgumentException("Cannot compute a centroid of no points.", nameof(points));

        double lat = 0, lng = 0;
        foreach (var p in points)
        {
            lat += p.Position.Latitude;
            lng += p.Position.Longitude;
        }
        return GeoPoint.FromLatLng(lat / points.Count, lng / points.Count);
    }
}
