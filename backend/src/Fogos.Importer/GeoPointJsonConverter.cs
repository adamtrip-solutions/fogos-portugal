using System.Text.Json;
using System.Text.Json.Serialization;
using Fogos.Domain.Geo;

namespace Fogos.Importer;

/// <summary>Reads/writes <see cref="GeoPoint"/> as a GeoJSON <c>[lng, lat]</c> array in seed fixtures.</summary>
public sealed class GeoPointJsonConverter : JsonConverter<GeoPoint>
{
    public override GeoPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var coordinates = JsonSerializer.Deserialize<double[]>(ref reader, options)
            ?? throw new JsonException("Expected a [lng, lat] coordinate array.");
        return GeoPoint.FromGeoJson(coordinates);
    }

    public override void Write(Utf8JsonWriter writer, GeoPoint value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Longitude);
        writer.WriteNumberValue(value.Latitude);
        writer.WriteEndArray();
    }
}
