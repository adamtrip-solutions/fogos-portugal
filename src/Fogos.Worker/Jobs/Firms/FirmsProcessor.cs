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
/// </summary>
public sealed class FirmsProcessor(
    MongoContext mongo,
    FirmsClient firms,
    ILogger<FirmsProcessor> logger)
{
    private const string ViirsSource = "VIIRS_SNPP_NRT";
    private const string ModisSource = "MODIS_NRT";
    private static readonly ReplaceOptions Upsert = new() { IsUpsert = true };

    /// <summary>Processes all active fire incidents with coordinates; returns the number processed.</summary>
    public async Task<int> ProcessAsync(CancellationToken ct = default)
    {
        var f = Builders<Incident>.Filter;
        var filter = f.Eq(x => x.Active, true)
                     & f.Eq(x => x.Kind, IncidentKind.Fire)
                     & f.Ne(x => x.Coordinates, null);

        var incidents = await mongo.Incidents.Find(filter).ToListAsync(ct);
        if (incidents.Count == 0)
            return 0;

        foreach (var incident in incidents)
            await ProcessIncidentAsync(incident, ct);

        return incidents.Count;
    }

    private async Task ProcessIncidentAsync(Incident incident, CancellationToken ct)
    {
        var point = incident.Coordinates!.Value;
        var bbox = FirmsBbox.Around(point);

        var viirs = await FetchAsync(ViirsSource, bbox, incident.Id, ct);
        var modis = await FetchAsync(ModisSource, bbox, incident.Id, ct);

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
            FetchedAt = DateTimeOffset.UtcNow,
        };

        await mongo.Hotspots.ReplaceOneAsync(
            Builders<Hotspots>.Filter.Eq(x => x.IncidentId, incident.Id), doc, Upsert, ct);

        logger.LogDebug("FIRMS incident={Id} viirs={Viirs} modis={Modis}",
            incident.Id, doc.Viirs.Count, doc.Modis.Count);
    }

    /// <summary>Fetch+parse one source; returns null on HTTP/parse failure so the caller can preserve prior data.</summary>
    private async Task<List<HotspotSample>?> FetchAsync(string source, string bbox, string incidentId, CancellationToken ct)
    {
        try
        {
            var csv = await firms.GetAreaCsvAsync(source, bbox, dayRange: 1, ct);
            return FirmsCsvParser.Parse(csv).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FIRMS {Source} fetch failed for incident {Id}.", source, incidentId);
            return null;
        }
    }
}
