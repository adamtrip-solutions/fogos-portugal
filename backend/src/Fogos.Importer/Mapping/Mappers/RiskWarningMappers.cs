using Fogos.Domain.Risk;
using Fogos.Domain.Time;
using Fogos.Domain.Warnings;
using MongoDB.Bson;
using static Fogos.Importer.Mapping.LegacyBson;

namespace Fogos.Importer.Mapping.Mappers;

/// <summary><c>rcm</c> → <c>rcm_daily</c>: fire risk per concelho per forecast day.</summary>
public sealed class RcmDailyMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "rcm";
    public string TargetDescription => "rcm_daily";

    public MapResult Map(BsonDocument doc)
    {
        var date = ReadDate(GetAny(doc, "date", "created"), clock);
        if (date is null)
            return MapResult.Quarantine("no date");

        var risk = new ConcelhoRisk
        {
            Id = CarryObjectId(doc),
            Dico = PadDico(Get(doc, "dico")),
            Concelho = ReadString(Get(doc, "concelho")) ?? "",
            Date = DateOnly.FromDateTime(date.Value.UtcDateTime),
            Today = ReadInt(Get(doc, "hoje")),
            Tomorrow = ReadInt(Get(doc, "amanha")),
            After = ReadInt(Get(doc, "depois")),
            After2 = ReadInt(Get(doc, "depois2")),
            After3 = ReadInt(Get(doc, "depois3")),
        };
        return MapResult.Map(new MappedEntity(TargetCollections.RcmDaily, risk));
    }
}

/// <summary>
/// <c>rcmJS</c> → <c>rcm_geojson</c>: the pre-built per-horizon risk payload, kept verbatim.
/// <c>when</c> picks the horizon; the remaining payload (minus the mapped scalars) is the GeoJSON.
/// </summary>
public sealed class RcmGeoJsonMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "rcmJS";
    public string TargetDescription => "rcm_geojson";

    private static readonly string[] MappedFields = ["_id", "when", "dataPrev", "dataRun", "created", "updated"];

    public MapResult Map(BsonDocument doc)
    {
        var when = ReadString(Get(doc, "when"))?.ToLowerInvariant() switch
        {
            "hoje" => RiskDay.Today,
            "amanha" => RiskDay.Tomorrow,
            "depois" => RiskDay.After,
            _ => (RiskDay?)null,
        };
        if (when is null)
            return MapResult.Quarantine("unknown `when` horizon (expected hoje/amanha/depois)");

        var forecast = ReadDate(GetAny(doc, "dataPrev", "fileDate"), clock);
        if (forecast is null)
            return MapResult.Quarantine("no forecast date (missing dataPrev)");

        var payload = new BsonDocument(doc.Where(e => !MappedFields.Contains(e.Name)));

        var geo = new RiskGeoJson
        {
            Id = CarryObjectId(doc),
            When = when.Value,
            ForecastDate = DateOnly.FromDateTime(forecast.Value.UtcDateTime),
            RunAt = ReadDate(Get(doc, "dataRun"), clock),
            GeoJson = payload.ToJson(),
            UpdatedAt = ReadDate(Get(doc, "updated"), clock) ?? default,
        };
        return MapResult.Map(new MappedEntity(TargetCollections.RcmGeoJson, geo));
    }
}

/// <summary>
/// <c>warning</c> / <c>warning_agif</c> / <c>warningSite</c> → the single <c>warnings</c> collection,
/// differing only by <see cref="WarningKind"/>. Message comes from the legacy <c>text</c> field.
/// </summary>
public sealed class WarningMapper(string legacyName, WarningKind kind, IClock clock) : ILegacyCollectionMapper
{
    public string Name => legacyName;
    public string TargetDescription => $"warnings (kind={kind})";

    public MapResult Map(BsonDocument doc)
    {
        var message = ReadString(GetAny(doc, "text", "message"));
        if (message is null)
            return MapResult.Quarantine("no message (missing text)");

        var warning = new Warning
        {
            Id = CarryObjectId(doc),
            Kind = kind,
            Message = message,
            Url = ReadString(Get(doc, "url")),
            IssuedBy = ReadString(GetAny(doc, "issuedBy", "issued_by")),
            CreatedAt = ReadDate(GetAny(doc, "created", "updated"), clock) ?? clock.UtcNow,
        };
        return MapResult.Map(new MappedEntity(TargetCollections.Warnings, warning));
    }
}
