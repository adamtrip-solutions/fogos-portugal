using Fogos.Domain.Aircraft;
using Fogos.Domain.Geo;
using Fogos.Domain.Locations;
using Fogos.Domain.Stats;
using Fogos.Domain.Time;
using MongoDB.Bson;
using static Fogos.Importer.Mapping.LegacyBson;

namespace Fogos.Importer.Mapping.Mappers;

/// <summary><c>flight_positions</c> → <c>flight_positions</c>: append-only aircraft samples.</summary>
public sealed class FlightPositionMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "flight_positions";
    public string TargetDescription => "flight_positions";

    public MapResult Map(BsonDocument doc)
    {
        var icao = ReadString(Get(doc, "icao"));
        var registration = ReadString(Get(doc, "registration"));
        if (icao is null || registration is null)
            return MapResult.Quarantine("missing icao/registration");

        var lat = ReadDouble(GetAny(doc, "lat", "latitude"));
        var lng = ReadDouble(GetAny(doc, "lon", "lng", "longitude"));
        var point = MakePoint(lat, lng);
        if (point is null)
            return MapResult.Quarantine("no/invalid position (lat/lon)");

        var sampledAt = ReadDate(GetAny(doc, "sampled_at", "created"), clock);
        if (sampledAt is null)
            return MapResult.Quarantine("no sampled_at/created");

        var position = new FlightPosition
        {
            Id = CarryObjectId(doc),
            Icao = icao,
            Registration = registration,
            Position = point.Value,
            Altitude = ReadDouble(Get(doc, "altitude")),
            SampledAt = sampledAt.Value,
            Source = ReadString(Get(doc, "source")) ?? "",
            Fr24Id = ReadString(Get(doc, "fr24_id")),
        };
        return MapResult.Map(new MappedEntity(TargetCollections.FlightPositions, position));
    }
}

/// <summary><c>tracked_aircraft</c> → <c>tracked_aircraft</c>: the firefighting fleet whitelist. <c>_id</c> = ICAO.</summary>
public sealed class TrackedAircraftMapper : ILegacyCollectionMapper
{
    public string Name => "tracked_aircraft";
    public string TargetDescription => "tracked_aircraft";

    public MapResult Map(BsonDocument doc)
    {
        var icao = ReadString(Get(doc, "icao"));
        if (icao is null)
            return MapResult.Quarantine("no icao");
        var registration = ReadString(Get(doc, "registration"));
        if (registration is null)
            return MapResult.Quarantine("no registration");

        var aircraft = new TrackedAircraft
        {
            Icao = icao,
            Registration = registration,
            Name = ReadString(Get(doc, "name")),
            Type = ReadString(Get(doc, "type")),
            Kind = ReadString(Get(doc, "kind")),
            Base = ReadString(Get(doc, "base")),
            Operator = ReadString(Get(doc, "operator")),
            Notify = ReadBool(Get(doc, "notify")),
            Active = ReadBool(Get(doc, "active"), fallback: true),
        };
        return MapResult.Map(new MappedEntity(TargetCollections.TrackedAircraft, aircraft));
    }
}

/// <summary><c>locations</c> → <c>locations</c>: geocoding table, DICO precomputed for concelhos.</summary>
public sealed class LocationMapper : ILegacyCollectionMapper
{
    public string Name => "locations";
    public string TargetDescription => "locations";

    public MapResult Map(BsonDocument doc)
    {
        var level = ReadInt(Get(doc, "level"));
        if (level is not (1 or 2))
            return MapResult.Quarantine($"unknown location level ({level?.ToString() ?? "null"}; expected 1/2)");

        var code = ReadString(Get(doc, "code"));
        if (code is null)
            return MapResult.Quarantine("no code");
        var name = ReadString(Get(doc, "name"));
        if (name is null)
            return MapResult.Quarantine("no name");

        var location = new Location
        {
            Id = CarryObjectId(doc),
            Level = (LocationLevel)level.Value,
            Code = code,
            Name = name,
            Dico = level == 2 ? PadDico(BsonValue.Create(code)) : null,
        };
        return MapResult.Map(new MappedEntity(TargetCollections.Locations, location));
    }
}

/// <summary><c>historyTotal</c> → <c>history_totals</c>: rolling nationwide resource totals.</summary>
public sealed class HistoryTotalMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "historyTotal";
    public string TargetDescription => "history_totals";

    public MapResult Map(BsonDocument doc)
    {
        var at = ReadDate(GetAny(doc, "date", "created"), clock);
        if (at is null)
            return MapResult.Quarantine("no timestamp (missing date/created)");

        var total = new HistoryTotal
        {
            Id = CarryObjectId(doc),
            At = at.Value,
            Man = ReadInt(Get(doc, "man")) ?? 0,
            Terrain = ReadInt(Get(doc, "terrain")) ?? 0,
            Aerial = ReadInt(Get(doc, "aerial")) ?? 0,
            Total = ReadInt(Get(doc, "total")) ?? 0,
        };
        return MapResult.Map(new MappedEntity(TargetCollections.HistoryTotals, total));
    }
}

/// <summary>Dead/superseded legacy collections: recorded as skips (never written) only when requested.</summary>
public sealed class SkipMapper(string legacyName, string reason) : ILegacyCollectionMapper
{
    public string Name => legacyName;
    public string TargetDescription => $"(not ported: {reason})";

    public MapResult Map(BsonDocument doc) => MapResult.Skip(reason);
}
