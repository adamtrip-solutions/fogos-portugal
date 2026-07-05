using Fogos.Domain.Incidents;
using Fogos.Domain.Social;
using Fogos.Domain.Time;
using MongoDB.Bson;
using static Fogos.Importer.Mapping.LegacyBson;

namespace Fogos.Importer.Mapping.Mappers;

/// <summary>
/// <c>data</c> → <c>incidents</c> (+ <c>social_threads</c>). The centre of gravity of the whole
/// import: dual id/_id → single business <c>_id</c>, the [lat,lng] coordinate trap, dirty
/// status keys, five isX booleans → one Kind, and thread state split off the incident.
/// </summary>
public sealed class IncidentMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "data";
    public string TargetDescription => "incidents (+ social_threads)";

    public MapResult Map(BsonDocument doc)
    {
        // ── Business id: top-level string `id`, else a string `_id`; ObjectId-only → quarantine.
        var id = ReadString(Get(doc, "id"));
        if (id is null && doc.TryGetValue("_id", out var rawId) && rawId.BsonType == BsonType.String)
            id = rawId.AsString.Trim();
        if (string.IsNullOrEmpty(id))
            return MapResult.Quarantine("no business id (missing `id` and no string `_id`)");

        // ── When ──────────────────────────────────────────────────────────────
        var created = ReadDate(GetAny(doc, "created"), clock);
        var updated = ReadDate(GetAny(doc, "updated"), clock);
        var occurredAt = ReadDate(Get(doc, "dateTime"), clock) ?? created ?? updated;
        if (occurredAt is null)
            return MapResult.Quarantine("no occurredAt (missing dateTime/created/updated)");

        // ── Status: statusCode authoritative, else normalize the dirty label ────
        IncidentStatus? status = null;
        if (ReadInt(Get(doc, "statusCode")) is { } code)
            status = IncidentStatusCatalog.FromCode(code);
        else if (ReadString(Get(doc, "status")) is { } label && IncidentStatusCatalog.TryNormalize(label, out var s))
            status = s;
        if (status is null)
            return MapResult.Quarantine("unmappable status (no statusCode and unrecognized status label)");

        // ── Kind: natureza code when present, else derive from the isX booleans ─
        var naturezaCode = ReadString(Get(doc, "naturezaCode")) ?? "";
        var kind = naturezaCode.Length > 0
            ? NaturezaCatalog.Classify(naturezaCode)
            : DeriveKind(doc);

        // ── Coordinates: prefer lat/lng, else the [lat,lng] array ──────────────
        var coords = MakePoint(ReadDouble(Get(doc, "lat")), ReadDouble(Get(doc, "lng")));
        if (coords is null && ReadPair(Get(doc, "coordinates")) is { } pair)
            coords = MakePoint(pair.A, pair.B); // legacy incident array is [lat, lng]

        var incident = new Incident
        {
            Id = id,
            OccurredAt = occurredAt.Value,
            CreatedAt = created ?? occurredAt.Value,
            UpdatedAt = updated ?? occurredAt.Value,
            Location = ReadString(Get(doc, "location")) ?? "",
            DetailLocation = ReadString(GetAny(doc, "detailLocation", "endereco")),
            District = ReadString(Get(doc, "district")) ?? "",
            Concelho = ReadString(Get(doc, "concelho")) ?? "",
            Freguesia = ReadString(Get(doc, "freguesia")),
            Dico = PadDico(Get(doc, "dico")),
            Region = ReadString(Get(doc, "regiao")),
            SubRegion = ReadString(Get(doc, "sub_regiao")),
            Coordinates = coords,
            Status = status,
            Kind = kind,
            NaturezaCode = naturezaCode,
            Natureza = ReadString(Get(doc, "natureza")) ?? "",
            Resources = ReadResources(doc),
            Active = Get(doc, "active") is { } a ? ReadBool(a) : IncidentStatusCatalog.IsActive(status.Code),
            Important = ReadBool(Get(doc, "important")),
            Extra = ReadString(Get(doc, "extra")),
            Icnf = ReadIcnf(doc, clock),
            Kml = ReadString(Get(doc, "kml")),
            KmlVost = ReadString(Get(doc, "kmlVost")),
            NearestWeatherStationId = ReadInt(Get(doc, "nearestWeatherStationId")),
            ArcGis = ReadArcGis(doc, clock),
        };

        var thread = ReadSocialThread(id, doc, incident.UpdatedAt);
        return thread is null
            ? MapResult.Map(new MappedEntity(TargetCollections.Incidents, incident))
            : MapResult.Map(
                new MappedEntity(TargetCollections.Incidents, incident),
                new MappedEntity(TargetCollections.SocialThreads, thread));
    }

    private static IncidentKind DeriveKind(BsonDocument doc)
    {
        if (ReadBool(Get(doc, "isFire"))) return IncidentKind.Fire;
        if (ReadBool(Get(doc, "isUrbanFire"))) return IncidentKind.UrbanFire;
        if (ReadBool(Get(doc, "isTransporteFire"))) return IncidentKind.TransportFire;
        if (ReadBool(Get(doc, "isOtherFire"))) return IncidentKind.OtherFire;
        if (ReadBool(Get(doc, "isFMA"))) return IncidentKind.Fma;
        return IncidentKind.Other;
    }

    private static Resources ReadResources(BsonDocument doc) => new()
    {
        Man = ReadInt(Get(doc, "man")) ?? 0,
        Terrain = ReadInt(Get(doc, "terrain")) ?? 0,
        Aerial = ReadInt(Get(doc, "aerial")) ?? 0,
        Aquatic = ReadInt(Get(doc, "meios_aquaticos")) ?? 0,
        ManGround = ReadInt(Get(doc, "operacionaisTerrestres")) ?? 0,
        ManAerial = ReadInt(Get(doc, "operacionaisAereos")) ?? 0,
        Entities = ReadInt(Get(doc, "quantEntidades")) ?? 0,
        HeliFight = ReadInt(Get(doc, "heliFight")) ?? 0,
        HeliCoord = ReadInt(Get(doc, "heliCoord")) ?? 0,
        PlaneFight = ReadInt(Get(doc, "planeFight")) ?? 0,
    };

    /// <summary>
    /// ICNF enrichment: burn area + cause taxonomy live in the <c>icnf</c> sub-doc (legacy field
    /// names <c>tipocausa/causafamilia/causa/fontealerta</c>); species/family are top-level.
    /// </summary>
    private static IcnfData? ReadIcnf(BsonDocument doc, IClock clock)
    {
        var icnf = Get(doc, "icnf") is { BsonType: BsonType.Document } i ? i.AsBsonDocument : null;
        var species = ReadString(Get(doc, "especieName"));
        var family = ReadString(Get(doc, "familiaName"));
        if (icnf is null && species is null && family is null)
            return null;

        BurnArea? burn = null;
        if (icnf is not null && Get(icnf, "burnArea") is { BsonType: BsonType.Document } b)
        {
            var ba = b.AsBsonDocument;
            burn = new BurnArea(
                ReadDouble(Get(ba, "povoamento")),
                ReadDouble(Get(ba, "agricola")),
                ReadDouble(Get(ba, "mato")),
                ReadDouble(Get(ba, "total")));
        }

        return new IcnfData
        {
            BurnArea = burn,
            CauseType = icnf is null ? null : ReadString(Get(icnf, "tipocausa")),
            CauseFamily = icnf is null ? null : ReadString(Get(icnf, "causafamilia")),
            Cause = icnf is null ? null : ReadString(Get(icnf, "causa")),
            SpeciesName = species,
            FamilyName = family,
            AlertSource = icnf is null ? null : ReadString(GetAny(icnf, "fontealerta", "fonteAlerta", "fonte_alerta")),
            IcnfId = icnf is null ? null : ReadString(Get(icnf, "ncco")),
            UpdatedAt = icnf is null ? null : ReadDate(Get(icnf, "updated"), clock),
        };
    }

    private static ArcGisDetails? ReadArcGis(BsonDocument doc, IClock clock)
    {
        var estado = ReadString(Get(doc, "estadoAgrupado"));
        var fase = ReadString(Get(doc, "faseIncendio"));
        var rasi = Get(doc, "rasi");
        var duracao = Get(doc, "duracaoMinutos");
        var dataDados = ReadDate(Get(doc, "dataDosDados"), clock);
        if (estado is null && fase is null && rasi is null && duracao is null && dataDados is null)
            return null;

        return new ArcGisDetails
        {
            EstadoAgrupado = estado,
            FaseIncendio = fase,
            Rasi = rasi is null ? null : ReadBool(rasi),
            DuracaoMinutos = ReadInt(duracao),
            DataDosDados = dataDados,
        };
    }

    private static SocialThread? ReadSocialThread(string incidentId, BsonDocument doc, DateTimeOffset updatedAt)
    {
        var lastTweet = ReadString(Get(doc, "lastTweetId"));
        var facebook = ReadString(Get(doc, "facebookPostId"));
        var sentImportant = Get(doc, "sentCheckImportant");
        var notifyBig = Get(doc, "notifyBig");
        if (lastTweet is null && facebook is null && sentImportant is null && notifyBig is null)
            return null;

        return new SocialThread
        {
            IncidentId = incidentId,
            LastTweetId = lastTweet,
            FacebookPostId = facebook,
            SentImportantPost = ReadBool(sentImportant),
            SentBigIncidentPost = ReadBool(notifyBig),
            UpdatedAt = updatedAt,
        };
    }
}
