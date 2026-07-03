using Fogos.Domain.Geo;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Fogos.Infrastructure.Mongo;

/// <summary>Stores <see cref="DateTimeOffset"/> as a BSON UTC DateTime; reads back as a UTC offset.</summary>
public sealed class DateTimeOffsetToUtcSerializer : SerializerBase<DateTimeOffset>
{
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DateTimeOffset value)
    {
        var ms = BsonUtils.ToMillisecondsSinceEpoch(value.UtcDateTime);
        context.Writer.WriteDateTime(ms);
    }

    public override DateTimeOffset Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        var ms = reader.ReadDateTime();
        var utc = DateTime.SpecifyKind(BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(ms), DateTimeKind.Utc);
        return new DateTimeOffset(utc);
    }
}

/// <summary>Stores <see cref="DateOnly"/> as a BSON DateTime at midnight UTC.</summary>
public sealed class DateOnlyToUtcSerializer : SerializerBase<DateOnly>
{
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, DateOnly value)
    {
        var midnightUtc = new DateTime(value.Year, value.Month, value.Day, 0, 0, 0, DateTimeKind.Utc);
        context.Writer.WriteDateTime(BsonUtils.ToMillisecondsSinceEpoch(midnightUtc));
    }

    public override DateOnly Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var ms = context.Reader.ReadDateTime();
        return DateOnly.FromDateTime(BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(ms));
    }
}

/// <summary>
/// Maps <see cref="GeoPoint"/> to/from a GeoJSON Point sub-document
/// <c>{ type: "Point", coordinates: [lng, lat] }</c> — the only stored coordinate shape.
/// </summary>
public sealed class GeoPointSerializer : SerializerBase<GeoPoint>
{
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, GeoPoint value)
    {
        var writer = context.Writer;
        writer.WriteStartDocument();
        writer.WriteName("type");
        writer.WriteString("Point");
        writer.WriteName("coordinates");
        writer.WriteStartArray();
        writer.WriteDouble(value.Longitude);
        writer.WriteDouble(value.Latitude);
        writer.WriteEndArray();
        writer.WriteEndDocument();
    }

    public override GeoPoint Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        double lng = 0, lat = 0;
        reader.ReadStartDocument();
        while (reader.ReadBsonType() != BsonType.EndOfDocument)
        {
            var name = reader.ReadName();
            switch (name)
            {
                case "coordinates":
                    reader.ReadStartArray();
                    lng = ReadNumber(reader);
                    lat = ReadNumber(reader);
                    while (reader.ReadBsonType() != BsonType.EndOfDocument)
                        reader.SkipValue();
                    reader.ReadEndArray();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.ReadEndDocument();
        return GeoPoint.FromGeoJson(new[] { lng, lat });
    }

    private static double ReadNumber(IBsonReader reader) => reader.GetCurrentBsonType() switch
    {
        BsonType.Int32 => reader.ReadInt32(),
        BsonType.Int64 => reader.ReadInt64(),
        _ => reader.ReadDouble(),
    };
}
