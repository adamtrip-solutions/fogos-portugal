using System.Text.Json;
using Fogos.Domain.Geo;
using Fogos.Domain.Time;
using Fogos.Domain.Weather;

namespace Fogos.Worker.Jobs.Weather.Parsing;

/// <summary>
/// Parses IPMA <c>stations.json</c> (a GeoJSON feature list) into <see cref="WeatherStation"/>s.
/// Port of <c>UpdateWeatherStations.php</c>: id = <c>properties.idEstacao</c>, name =
/// <c>properties.localEstacao</c>, coordinates = <c>geometry.coordinates</c> which IPMA gives as
/// <b>[lng, lat]</b> (GeoJSON order). Place is derived from the coordinates exactly as legacy did:
/// lat &lt; 34 → Madeira; lng &lt; -20 &amp;&amp; lat &gt; 34 → Açores; otherwise Portugal.
/// </summary>
public static class StationsParser
{
    public static IReadOnlyList<WeatherStation> Parse(string json, IClock clock)
    {
        using var doc = JsonDocument.Parse(json);
        var stations = new List<WeatherStation>();

        foreach (var feature in Features(doc.RootElement))
        {
            var props = IpmaJson.Prop(feature, "properties");
            if (props is not { } p)
                continue;

            var id = IpmaJson.ReadInt(IpmaJson.Prop(p, "idEstacao"));
            if (id is null)
                continue;

            var geometry = IpmaJson.Prop(feature, "geometry");
            if (geometry is not { } g || IpmaJson.Prop(g, "coordinates") is not { ValueKind: JsonValueKind.Array } coords || coords.GetArrayLength() < 2)
                continue;

            var lng = IpmaJson.ReadNumber(coords[0]);
            var lat = IpmaJson.ReadNumber(coords[1]);
            if (lng is null || lat is null)
                continue;

            GeoPoint point;
            try
            {
                point = GeoPoint.FromGeoJson(lng.Value, lat.Value);
            }
            catch (ArgumentException)
            {
                continue;
            }

            stations.Add(new WeatherStation
            {
                Id = id.Value,
                Coordinates = point,
                Name = IpmaJson.ReadString(IpmaJson.Prop(p, "localEstacao")) ?? "",
                Place = DerivePlace(lng.Value, lat.Value),
                UpdatedAt = clock.UtcNow,
            });
        }

        return stations;
    }

    private static string DerivePlace(double lng, double lat) =>
        lat < 34 ? "Madeira"
        : lng < -20 && lat > 34 ? "Açores"
        : "Portugal";

    /// <summary>Accepts either a bare feature array (the real feed) or a GeoJSON FeatureCollection.</summary>
    private static IEnumerable<JsonElement> Features(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray();
        if (IpmaJson.Prop(root, "features") is { ValueKind: JsonValueKind.Array } features)
            return features.EnumerateArray();
        return [];
    }
}
