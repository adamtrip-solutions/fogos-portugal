using Fogos.Domain.Geo;
using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Time;
using MongoDB.Bson;
using static Fogos.Importer.Mapping.LegacyBson;

namespace Fogos.Importer.Mapping.Mappers;

/// <summary><c>history</c> → <c>incident_history</c>: resource snapshots keyed to an incident.</summary>
public sealed class IncidentHistoryMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "history";
    public string TargetDescription => "incident_history";

    public MapResult Map(BsonDocument doc)
    {
        var fk = ReadString(GetAny(doc, "incidentId", "id"));
        if (fk is null)
            return MapResult.Quarantine("no incident FK (missing incidentId/id)");

        var at = ReadDate(GetAny(doc, "created"), clock) ?? ReadDate(Get(doc, "updated"), clock);
        if (at is null)
            return MapResult.Quarantine("no timestamp (missing created/updated)");

        var snapshot = new IncidentHistorySnapshot
        {
            Id = CarryObjectId(doc),
            IncidentId = fk,
            At = at.Value,
            Man = ReadInt(Get(doc, "man")) ?? 0,
            Terrain = ReadInt(Get(doc, "terrain")) ?? 0,
            Aerial = ReadInt(Get(doc, "aerial")) ?? 0,
            Location = ReadString(Get(doc, "location")),
        };
        return MapResult.Map(new MappedEntity(TargetCollections.IncidentHistory, snapshot));
    }
}

/// <summary>
/// <c>statusHistory</c> → <c>incident_status_history</c>: status transitions. <c>created</c> may be a
/// <c>d-m-Y H:i</c> Lisbon string (the model's <c>$dateFormat</c>); labels normalize via the catalog.
/// </summary>
public sealed class StatusHistoryMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "statusHistory";
    public string TargetDescription => "incident_status_history";

    public MapResult Map(BsonDocument doc)
    {
        var fk = ReadString(GetAny(doc, "id", "incidentId"));
        if (fk is null)
            return MapResult.Quarantine("no incident FK (missing id/incidentId)");

        var at = ReadDate(GetAny(doc, "created"), clock) ?? ReadDate(Get(doc, "updated"), clock);
        if (at is null)
            return MapResult.Quarantine("no timestamp (missing created/updated)");

        IncidentStatus? status = null;
        if (ReadInt(Get(doc, "statusCode")) is { } code)
            status = IncidentStatusCatalog.FromCode(code);
        else if (ReadString(Get(doc, "status")) is { } label && IncidentStatusCatalog.TryNormalize(label, out var s))
            status = s;
        if (status is null)
            return MapResult.Quarantine("unmappable status (no statusCode and unrecognized label)");

        var change = new IncidentStatusChange
        {
            Id = CarryObjectId(doc),
            IncidentId = fk,
            At = at.Value,
            Code = status.Code,
            Label = status.Label,
        };
        return MapResult.Map(new MappedEntity(TargetCollections.IncidentStatusHistory, change));
    }
}

/// <summary>
/// <c>incident_photos</c> → <c>incident_photos</c>. Note the systemic gotcha: this legacy model
/// uses <c>created_at</c>/<c>updated_at</c> (not <c>created</c>/<c>updated</c>). storage_key is required.
/// </summary>
public sealed class IncidentPhotoMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "incident_photos";
    public string TargetDescription => "incident_photos";

    public MapResult Map(BsonDocument doc)
    {
        var fk = ReadString(Get(doc, "fire_id"));
        if (fk is null)
            return MapResult.Quarantine("no incident FK (missing fire_id)");

        var storageKey = ReadString(Get(doc, "storage_key"));
        if (storageKey is null)
            return MapResult.Quarantine("no storage_key");

        var status = ReadString(Get(doc, "status"))?.ToLowerInvariant() switch
        {
            "approved" => ModerationStatus.Approved,
            "rejected" => ModerationStatus.Rejected,
            _ => ModerationStatus.Pending,
        };

        var photo = new IncidentPhoto
        {
            Id = CarryObjectId(doc),
            IncidentId = fk,
            Status = status,
            Public = ReadBool(Get(doc, "public")),
            Signature = ReadString(Get(doc, "signature")),
            StorageKey = storageKey,
            SizeBytes = ReadInt(Get(doc, "size_bytes")) ?? 0,
            Width = ReadInt(Get(doc, "width")) ?? 0,
            Height = ReadInt(Get(doc, "height")) ?? 0,
            Mime = ReadString(Get(doc, "mime")) ?? "image/jpeg",
            Gps = ReadGps(Get(doc, "gps")),
            TakenAt = ReadDate(Get(doc, "taken_at"), clock),
            Client = ReadString(Get(doc, "client")),
            Moderation = ReadModeration(Get(doc, "moderation"), clock),
            CreatedAt = ReadDate(Get(doc, "created_at"), clock) ?? clock.UtcNow,
            UpdatedAt = ReadDate(Get(doc, "updated_at"), clock) ?? clock.UtcNow,
        };
        return MapResult.Map(new MappedEntity(TargetCollections.IncidentPhotos, photo));
    }

    private static GeoPoint? ReadGps(BsonValue? v)
    {
        switch (v?.BsonType)
        {
            case BsonType.Document:
                var d = v!.AsBsonDocument;
                return MakePoint(ReadDouble(GetAny(d, "lat", "latitude")), ReadDouble(GetAny(d, "lng", "lon", "longitude")));
            case BsonType.Array when ReadPair(v) is { } pair:
                return MakePoint(pair.A, pair.B); // EXIF gps array is [lat, lng]
            default:
                return null;
        }
    }

    private static PhotoModeration? ReadModeration(BsonValue? v, IClock clock)
    {
        if (v is not { BsonType: BsonType.Document })
            return null;
        var d = v.AsBsonDocument;
        var at = ReadDate(GetAny(d, "at", "moderated_at", "created_at"), clock);
        var decision = ReadString(GetAny(d, "decision", "status"));
        if (at is null && decision is null)
            return null;
        return new PhotoModeration(at ?? clock.UtcNow, decision ?? "", ReadString(GetAny(d, "reason", "note")));
    }
}

/// <summary><c>hotspots</c> → <c>hotspots</c>: NASA FIRMS VIIRS/MODIS samples per incident.</summary>
public sealed class HotspotMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "hotspots";
    public string TargetDescription => "hotspots";

    public MapResult Map(BsonDocument doc)
    {
        var incidentId = ReadString(GetAny(doc, "incident_id", "incidentId"));
        if (incidentId is null)
            return MapResult.Quarantine("no incident id (missing incident_id)");

        var hotspots = new Hotspots
        {
            IncidentId = incidentId,
            Viirs = ReadSamples(Get(doc, "viirs"), clock),
            Modis = ReadSamples(Get(doc, "modis"), clock),
            FetchedAt = ReadDate(GetAny(doc, "fetched_at", "created"), clock) ?? clock.UtcNow,
        };
        return MapResult.Map(new MappedEntity(TargetCollections.Hotspots, hotspots));
    }

    private static List<HotspotSample> ReadSamples(BsonValue? v, IClock clock)
    {
        var list = new List<HotspotSample>();
        if (v is not { BsonType: BsonType.Array })
            return list;

        foreach (var el in v.AsBsonArray)
        {
            if (el is not { BsonType: BsonType.Document })
                continue;
            var s = el.AsBsonDocument;
            var point = MakePoint(
                ReadDouble(GetAny(s, "lat", "latitude")),
                ReadDouble(GetAny(s, "lng", "lon", "longitude")));
            if (point is null)
                continue;
            list.Add(new HotspotSample(
                point.Value,
                ReadAcquiredAt(s),
                ReadDouble(GetAny(s, "brightness", "bright", "bright_ti4")),
                ReadString(Get(s, "confidence"))));
        }
        return list;
    }

    /// <summary>FIRMS <c>acq_date</c> (YYYY-MM-DD) + <c>acq_time</c> (HHMM, UTC) → instant, when parseable.</summary>
    private static DateTimeOffset? ReadAcquiredAt(BsonDocument s)
    {
        var date = ReadString(Get(s, "acq_date"));
        if (date is null || !DateOnly.TryParse(date, out var d))
            return null;
        var time = ReadString(Get(s, "acq_time"))?.PadLeft(4, '0') ?? "0000";
        var hh = int.TryParse(time.Length >= 4 ? time[..2] : "0", out var h) ? h : 0;
        var mm = int.TryParse(time.Length >= 4 ? time.Substring(2, 2) : "0", out var m) ? m : 0;
        if (hh > 23 || mm > 59)
            return null;
        return new DateTimeOffset(d.Year, d.Month, d.Day, hh, mm, 0, TimeSpan.Zero);
    }
}
