using Fogos.Domain.Risk;
using Fogos.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Worker.Jobs.Risk;

/// <summary>
/// Core RCM ingest, independent of Quartz so it is directly fixture-testable. Parses the IPMA page,
/// upserts <c>rcm_daily</c> (one row per concelho, key = DICO+forecast date) and assembles and upserts
/// the three served <c>rcm_geojson</c> horizons (Today/Tomorrow/After).
/// </summary>
public sealed class RcmProcessor(
    MongoContext mongo,
    ConcelhoPolygons polygons,
    ILogger<RcmProcessor> logger)
{
    private static readonly ReplaceOptions Upsert = new() { IsUpsert = true };

    // Which RiskDay horizon each served GeoJSON maps to, and the parser index that feeds it.
    private static readonly (RiskDay Day, int Index)[] GeoJsonHorizons =
        [(RiskDay.Today, 0), (RiskDay.Tomorrow, 1), (RiskDay.After, 2)];

    /// <summary>
    /// Runs the full ingest for one page and returns the forecast date it wrote <c>rcm_daily</c> for
    /// (so the caller can announce <c>RcmProcessed</c> for risk-alert matching). <paramref name="page"/>
    /// is the raw JSP HTML.
    /// </summary>
    public async Task<DateOnly> ProcessAsync(string page, CancellationToken ct = default)
    {
        var parsed = RcmPageParser.Parse(page); // throws RcmParseException — the job classifies it.

        var rows = await UpsertDailyAsync(parsed, ct);
        await UpsertGeoJsonAsync(parsed, ct);

        logger.LogInformation("RCM ingest: {Count} concelhos for {Date}.", rows.Count, parsed.ForecastDate);

        return parsed.ForecastDate;
    }

    private async Task<List<ConcelhoRisk>> UpsertDailyAsync(RcmParseResult parsed, CancellationToken ct)
    {
        var today = parsed.Horizon(0);
        var tomorrow = parsed.Horizon(1);
        var after = parsed.Horizon(2);
        var after2 = parsed.Horizon(3);
        var after3 = parsed.Horizon(4);

        var rows = new List<ConcelhoRisk>(polygons.Concelhos.Count);
        foreach (var c in polygons.Concelhos)
        {
            var filter = Builders<ConcelhoRisk>.Filter.Eq(x => x.Dico, c.Dico)
                         & Builders<ConcelhoRisk>.Filter.Eq(x => x.Date, parsed.ForecastDate);
            var existing = await mongo.RcmDaily.Find(filter).FirstOrDefaultAsync(ct);

            var row = new ConcelhoRisk
            {
                Id = existing?.Id ?? ObjectId.GenerateNewId().ToString(),
                Dico = c.Dico,
                Concelho = c.Concelho,
                Date = parsed.ForecastDate,
                Today = today?.Level(c.Dico),
                Tomorrow = tomorrow?.Level(c.Dico),
                After = after?.Level(c.Dico),
                After2 = after2?.Level(c.Dico),
                After3 = after3?.Level(c.Dico),
            };

            await mongo.RcmDaily.ReplaceOneAsync(
                Builders<ConcelhoRisk>.Filter.Eq(x => x.Id, row.Id), row, Upsert, ct);
            rows.Add(row);
        }
        return rows;
    }

    private async Task UpsertGeoJsonAsync(RcmParseResult parsed, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (day, index) in GeoJsonHorizons)
        {
            var horizon = parsed.Horizon(index);
            if (horizon is null)
                continue;

            var geoJson = polygons.BuildHorizonGeoJson(horizon.Data);

            var filter = Builders<RiskGeoJson>.Filter.Eq(x => x.When, day)
                         & Builders<RiskGeoJson>.Filter.Eq(x => x.ForecastDate, parsed.ForecastDate);
            var existing = await mongo.RcmGeoJson.Find(filter).FirstOrDefaultAsync(ct);

            var doc = new RiskGeoJson
            {
                Id = existing?.Id ?? ObjectId.GenerateNewId().ToString(),
                When = day,
                ForecastDate = parsed.ForecastDate,
                RunAt = horizon.RunAt,
                GeoJson = geoJson,
                UpdatedAt = now,
            };

            await mongo.RcmGeoJson.ReplaceOneAsync(
                Builders<RiskGeoJson>.Filter.Eq(x => x.Id, doc.Id), doc, Upsert, ct);
        }
    }
}
