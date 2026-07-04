using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Sources;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Fogos.Worker.Jobs.Firms;

/// <summary>
/// Core FIRMS hotspot ingest, independent of Quartz for direct fixture testing. For each active fire
/// incident with coordinates it queries the VIIRS + MODIS area CSVs over a ±0.10° bbox (1-day NRT),
/// parses the rows, and upserts the incident's <c>hotspots</c> doc (<c>_id = incidentId</c>), replacing
/// the viirs/modis lists wholesale. A source whose fetch fails preserves its previously stored samples
/// (mirrors legacy), so a transient error never wipes good data.
///
/// Coverage guarantee: fires from the previous <see cref="BackfillWindow"/> that closed while the
/// worker was down (inactive, no hotspots doc yet) are backfilled once with a day range wide enough
/// to reach back to their start — full info for the last 72h even after an outage.
/// </summary>
public sealed class FirmsProcessor(
    MongoContext mongo,
    FirmsClient firms,
    Fogos.Domain.Time.IClock clock,
    ILogger<FirmsProcessor> logger)
{
    public static readonly TimeSpan BackfillWindow = TimeSpan.FromHours(72);

    private const string ViirsSource = "VIIRS_SNPP_NRT";
    private const string ModisSource = "MODIS_NRT";
    private static readonly ReplaceOptions Upsert = new() { IsUpsert = true };

    /// <summary>Processes active fires plus 72h-window backfills; returns the number processed.</summary>
    public async Task<int> ProcessAsync(CancellationToken ct = default)
    {
        var f = Builders<Incident>.Filter;
        var fireWithCoords = f.Eq(x => x.Kind, IncidentKind.Fire) & f.Ne(x => x.Coordinates, null);

        var active = await mongo.Incidents.Find(fireWithCoords & f.Eq(x => x.Active, true)).ToListAsync(ct);

        // Recently-closed fires that never got a hotspots doc (worker was down when they burned).
        var recentClosed = await mongo.Incidents
            .Find(fireWithCoords
                  & f.Eq(x => x.Active, false)
                  & f.Gte(x => x.OccurredAt, clock.UtcNow - BackfillWindow))
            .ToListAsync(ct);
        var covered = await CoveredIdsAsync(recentClosed, ct);
        var backfills = recentClosed.Where(i => !covered.Contains(i.Id)).ToList();

        if (active.Count == 0 && backfills.Count == 0)
            return 0;

        foreach (var incident in active)
            await ProcessIncidentAsync(incident, dayRange: 1, ct);

        foreach (var incident in backfills)
            await ProcessIncidentAsync(incident, DayRangeFor(incident), ct);

        if (backfills.Count > 0)
            logger.LogInformation("FIRMS backfilled {Count} recently-closed fires.", backfills.Count);

        return active.Count + backfills.Count;
    }

    /// <summary>Satellite window reaching back to the incident's start (FIRMS caps at 10; we need ≤ 4).</summary>
    private int DayRangeFor(Incident incident) =>
        Math.Clamp((int)Math.Ceiling((clock.UtcNow - incident.OccurredAt).TotalDays) + 1, 1, 4);

    private async Task<HashSet<string>> CoveredIdsAsync(List<Incident> incidents, CancellationToken ct)
    {
        if (incidents.Count == 0)
            return [];
        var ids = incidents.Select(i => i.Id).ToList();
        var docs = await mongo.Hotspots
            .Find(Builders<Hotspots>.Filter.In(x => x.IncidentId, ids))
            .Project(x => x.IncidentId)
            .ToListAsync(ct);
        return [.. docs];
    }

    private async Task ProcessIncidentAsync(Incident incident, int dayRange, CancellationToken ct)
    {
        var point = incident.Coordinates!.Value;
        var bbox = FirmsBbox.Around(point);

        var viirs = await FetchAsync(ViirsSource, bbox, incident.Id, dayRange, ct);
        var modis = await FetchAsync(ModisSource, bbox, incident.Id, dayRange, ct);

        if (viirs is null && modis is null)
        {
            // Both sources failed — preserve whatever is already stored.
            logger.LogWarning("FIRMS: both sources failed for incident {Id}; preserving existing data.", incident.Id);
            return;
        }

        var existing = await mongo.Hotspots
            .Find(Builders<Hotspots>.Filter.Eq(x => x.IncidentId, incident.Id))
            .FirstOrDefaultAsync(ct);

        var doc = new Hotspots
        {
            IncidentId = incident.Id,
            Viirs = viirs ?? existing?.Viirs ?? [],
            Modis = modis ?? existing?.Modis ?? [],
            FetchedAt = clock.UtcNow,
        };

        await mongo.Hotspots.ReplaceOneAsync(
            Builders<Hotspots>.Filter.Eq(x => x.IncidentId, incident.Id), doc, Upsert, ct);

        logger.LogDebug("FIRMS incident={Id} viirs={Viirs} modis={Modis}",
            incident.Id, doc.Viirs.Count, doc.Modis.Count);
    }

    /// <summary>Fetch+parse one source; returns null on HTTP/parse failure so the caller can preserve prior data.</summary>
    private async Task<List<HotspotSample>?> FetchAsync(string source, string bbox, string incidentId, int dayRange, CancellationToken ct)
    {
        try
        {
            var csv = await firms.GetAreaCsvAsync(source, bbox, dayRange, ct);
            return FirmsCsvParser.Parse(csv).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FIRMS {Source} fetch failed for incident {Id}.", source, incidentId);
            return null;
        }
    }
}
