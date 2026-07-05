using Fogos.Domain.Risk;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Rendering;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Worker.Jobs.Risk;

/// <summary>
/// Core RCM ingest+publish, independent of Quartz so it is directly fixture-testable. Parses the IPMA
/// page, upserts <c>rcm_daily</c> (one row per concelho, key = DICO+forecast date), assembles and
/// upserts the three served <c>rcm_geojson</c> horizons (Today/Tomorrow/After), and — when asked —
/// composes and dry-run-publishes the risk-map post with an optional renderer screenshot.
/// </summary>
public sealed class RcmProcessor(
    MongoContext mongo,
    ConcelhoPolygons polygons,
    ITwitterPublisher twitter,
    ITelegramPublisher telegram,
    IFacebookPublisher facebook,
    RendererClient renderer,
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
    public async Task<DateOnly> ProcessAsync(string page, bool publishSocial, bool tomorrow, CancellationToken ct = default)
    {
        var parsed = RcmPageParser.Parse(page); // throws RcmParseException — the job classifies it.

        var rows = await UpsertDailyAsync(parsed, ct);
        await UpsertGeoJsonAsync(parsed, ct);

        logger.LogInformation("RCM ingest: {Count} concelhos for {Date} (social={Social}, tomorrow={Tomorrow}).",
            rows.Count, parsed.ForecastDate, publishSocial, tomorrow);

        if (publishSocial)
            await PublishAsync(parsed, rows, tomorrow, ct);

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

    private async Task PublishAsync(RcmParseResult parsed, IReadOnlyList<ConcelhoRisk> rows, bool tomorrow, CancellationToken ct)
    {
        var day = tomorrow ? RiskDay.Tomorrow : RiskDay.Today;
        var levels = rows
            .Select(r => (r.Concelho, Level: r.For(day)))
            .Where(x => x.Level is >= 1 and <= 5)
            .Select(x => (x.Concelho, x.Level!.Value));

        var text = RiskPostComposer.ComposeRiskMap(parsed.ForecastDate, tomorrow, levels);

        // Legacy captured the risk map at pt?risk=1 / pt?risk-tomorrow=1; degrade to text-only on failure.
        var mapPath = tomorrow ? "pt?risk-tomorrow=1" : "pt?risk=1";
        byte[]? image;
        try
        {
            image = await renderer.CaptureAsync(mapPath, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RCM map render failed; posting text-only.");
            image = null;
        }

        var post = new SocialPost { Text = text, ImageBytes = image };

        // Publishers never throw and default to DryRun (captured to ops). Fire all channels.
        await twitter.PublishAsync(post, ct: ct);
        await telegram.PublishAsync(post, ct: ct);
        await facebook.PublishAsync(post, ct: ct);
    }
}
