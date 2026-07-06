using System.Reflection;
using System.Text.Json;

namespace Fogos.Infrastructure.Geo;

/// <summary>A concelho's identity resolved from a coordinate: raw (uppercase) DICO, name, district as
/// published in the polygon set. Callers title-case/pad as needed (mirrors the polygon properties verbatim).</summary>
public sealed record ConcelhoMatch(string Dico, string Concelho, string Distrito);

/// <summary>
/// Point-in-polygon lookup over the 278 mainland concelho polygons embedded as
/// <c>Geo/ConcelhoPolygons.geojson</c> (the same resource the RCM job's <c>ConcelhoPolygons</c> reads for
/// GeoJSON assembly). Used by <c>LocationResolver</c> as the coordinate fallback when a concelho name
/// misses the <c>locations</c> table: the ANEPC ArcGIS feed carries no DICO, so we infer the administrative
/// unit from the incident's lon/lat rather than dropping the incident ("most information, not most correct").
/// <para>
/// Loaded once, lazily and thread-safely (mirrors <c>ConcelhoPolygons</c>' <see cref="Lazy{T}"/> pattern).
/// Each feature carries a precomputed bounding box so the common miss short-circuits before any ring walk.
/// Containment uses the ray-casting (even-odd) rule counting crossings over ALL rings of a polygon, which
/// makes holes fall out for free; Polygon and MultiPolygon geometries are both handled.
/// </para>
/// </summary>
public sealed class ConcelhoLocator
{
    private const string ResourceSuffix = "Geo.ConcelhoPolygons.geojson";

    private static readonly Lazy<string> RawJson = new(LoadRaw, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<IReadOnlyList<Feature>> Features =
        new(LoadFeatures, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The raw embedded GeoJSON text, loaded once. Exposed so <c>ConcelhoPolygons</c> (in Fogos.Worker) reads
    /// the single shared resource from this assembly instead of keeping its own copy of the file.
    /// </summary>
    public static string RawGeoJson => RawJson.Value;

    /// <summary>
    /// The concelho whose polygon contains <paramref name="lat"/>/<paramref name="lng"/> (WGS84 degrees), or
    /// null when the point falls outside every mainland concelho (Atlantic, Spain, the islands, bad fix).
    /// </summary>
    public ConcelhoMatch? Locate(double lat, double lng)
    {
        foreach (var feature in Features.Value)
        {
            // Bounding-box reject first: for a single point over 278 features this skips almost all ring walks.
            if (lng < feature.MinLng || lng > feature.MaxLng || lat < feature.MinLat || lat > feature.MaxLat)
                continue;
            if (feature.Contains(lng, lat))
                return feature.Match;
        }
        return null;
    }

    /// <summary>Ray-casting inclusion of a point in one closed ring (ordered lon/lat vertices).</summary>
    private static bool RingContains(double[][] ring, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            double xi = ring[i][0], yi = ring[i][1];
            double xj = ring[j][0], yj = ring[j][1];
            // Does the upward ray from (x,y) cross edge j→i? (half-open [yi,yj) avoids double-counting vertices)
            if (((yi > y) != (yj > y)) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    private static IReadOnlyList<Feature> LoadFeatures()
    {
        using var doc = JsonDocument.Parse(RawJson.Value);
        var list = new List<Feature>();
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var props = feature.GetProperty("properties");
            var match = new ConcelhoMatch(
                props.GetProperty("DICO").GetString()!,
                props.GetProperty("Concelho").GetString()!,
                props.GetProperty("Distrito").GetString()!);

            var geometry = feature.GetProperty("geometry");
            var type = geometry.GetProperty("type").GetString();
            var coords = geometry.GetProperty("coordinates");

            // Normalize both geometry kinds to a list of polygons, each polygon a list of rings.
            var polygons = type switch
            {
                "Polygon" => [ReadPolygon(coords)],
                "MultiPolygon" => coords.EnumerateArray().Select(ReadPolygon).ToList(),
                _ => throw new InvalidOperationException($"Unsupported geometry '{type}' for DICO {match.Dico}."),
            };

            list.Add(Feature.From(match, polygons));
        }
        return list;
    }

    private static double[][][] ReadPolygon(JsonElement polygon) =>
        polygon.EnumerateArray()
            .Select(ring => ring.EnumerateArray()
                .Select(pt => new[] { pt[0].GetDouble(), pt[1].GetDouble() })
                .ToArray())
            .ToArray();

    private static string LoadRaw()
    {
        var assembly = typeof(ConcelhoLocator).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded concelho polygon resource '*{ResourceSuffix}' not found.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>One concelho: its identity, its polygons (each a set of rings), and a bounding box for rejection.</summary>
    private sealed class Feature
    {
        public required ConcelhoMatch Match { get; init; }
        public required double[][][][] Polygons { get; init; } // [polygon][ring][vertex][lng,lat]
        public double MinLng { get; private init; }
        public double MinLat { get; private init; }
        public double MaxLng { get; private init; }
        public double MaxLat { get; private init; }

        /// <summary>Point-in-feature: inside iff the point is inside any one polygon (even-odd over that polygon's rings).</summary>
        public bool Contains(double x, double y)
        {
            foreach (var polygon in Polygons)
            {
                // Even-odd across all of a polygon's rings: a hit inside a hole flips back to outside.
                var inside = false;
                foreach (var ring in polygon)
                    if (RingContains(ring, x, y))
                        inside = !inside;
                if (inside)
                    return true;
            }
            return false;
        }

        public static Feature From(ConcelhoMatch match, List<double[][][]> polygons)
        {
            double minLng = double.MaxValue, minLat = double.MaxValue, maxLng = double.MinValue, maxLat = double.MinValue;
            foreach (var ring in polygons.SelectMany(p => p))
                foreach (var pt in ring)
                {
                    if (pt[0] < minLng) minLng = pt[0];
                    if (pt[0] > maxLng) maxLng = pt[0];
                    if (pt[1] < minLat) minLat = pt[1];
                    if (pt[1] > maxLat) maxLat = pt[1];
                }

            return new Feature
            {
                Match = match,
                Polygons = polygons.ToArray(),
                MinLng = minLng,
                MinLat = minLat,
                MaxLng = maxLng,
                MaxLat = maxLat,
            };
        }
    }
}
