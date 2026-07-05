using System.Text;
using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Sources;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Fogos.Worker.Jobs.Icnf;

/// <summary>
/// Core of <c>ProcessICNFFireData</c>: fetch the ICNF XML detail, merge the <c>icnf</c> sub-document
/// (burn area, cause taxonomy, alert source, ncco), download the KML perimeter onto the incident, and
/// detect first-seen cause / KML / burn-area. Raises <see cref="IcnfEnriched"/> (on the <c>icnf</c>
/// stream) when any first-seen signal fires, so the social handler can post. Re-fetches the incident
/// before writing (lost-update protection).
/// </summary>
public sealed class IcnfEnrichmentService(
    IcnfClient client,
    MongoContext mongo,
    IEventDispatcher dispatcher,
    IClock clock,
    Fogos.Infrastructure.Incidents.KmlVersionStore kmlVersions,
    ILogger<IcnfEnrichmentService> logger)
{
    public async Task<IcnfEnriched?> EnrichAsync(string incidentId, string? icnfId, CancellationToken ct = default)
    {
        var ncco = string.IsNullOrEmpty(icnfId) ? incidentId : icnfId;

        string xml;
        try
        {
            xml = await client.FetchOccurrenceXmlAsync(ncco, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ICNF XML fetch failed for {Id}.", incidentId);
            return null;
        }

        var occ = IcnfOccurrenceXml.Parse(xml);
        if (occ is null)
            return null;

        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, incidentId))
            .FirstOrDefaultAsync(ct);
        if (incident is null)
            return null;

        var stored = incident.Icnf;

        // ── Burn area ──────────────────────────────────────────────────────────
        BurnArea? burnArea = null;
        var firstBurnArea = false;
        if (occ.AreaTotal is { } total && total != 0.0)
        {
            burnArea = new BurnArea(occ.AreaPovoamento, occ.AreaAgricola, occ.AreaMato, total);
            firstBurnArea = stored?.BurnArea is null;
        }

        // ── Cause / source ─────────────────────────────────────────────────────
        var causeNew = occ.Causa is not null && stored?.Cause != occ.Causa;
        var sourceNew = occ.FonteAlerta is not null && stored?.AlertSource != occ.FonteAlerta;
        var firstCause = causeNew || sourceNew;

        // ── KML perimeter ──────────────────────────────────────────────────────
        var firstKml = false;
        string? kml = null;
        if (occ.KmlUrl is not null)
        {
            var bytes = await client.DownloadKmlAsync(ncco, ct);
            if (bytes is { Length: > 0 })
            {
                kml = Encoding.UTF8.GetString(bytes);
                firstKml = string.IsNullOrEmpty(incident.Kml);
            }
        }

        var merged = new IcnfData
        {
            BurnArea = burnArea ?? stored?.BurnArea,
            CauseType = occ.TipoCausa ?? stored?.CauseType,
            CauseFamily = occ.CausaFamilia ?? stored?.CauseFamily,
            Cause = occ.Causa ?? stored?.Cause,
            SpeciesName = stored?.SpeciesName,
            FamilyName = stored?.FamilyName,
            AlertSource = occ.FonteAlerta ?? stored?.AlertSource,
            IcnfId = icnfId ?? stored?.IcnfId ?? incidentId,
            UpdatedAt = clock.UtcNow,
        };

        var update = Builders<Incident>.Update
            .Set(x => x.Icnf, merged)
            .Set(x => x.UpdatedAt, clock.UtcNow);
        if (occ.Local is not null)
            update = update.Set(x => x.DetailLocation, occ.Local);
        if (kml is not null)
            update = update.Set(x => x.Kml, kml);

        await mongo.Incidents.UpdateOneAsync(Builders<Incident>.Filter.Eq(x => x.Id, incidentId), update, cancellationToken: ct);

        // Version the downloaded perimeter (dedup by SHA-256; ICNF only ever writes the non-VOST slot).
        if (kml is not null)
            await kmlVersions.AppendIfChangedAsync(incidentId, vost: false, kml, ct);

        if (!firstCause && !firstKml && !firstBurnArea)
            return null;

        var enriched = new IcnfEnriched(incidentId, firstCause, firstKml, firstBurnArea);
        await dispatcher.DispatchAsync(enriched, IcnfKickoffStream, ct);
        return enriched;
    }

    /// <summary>Social follow-ups stay on the icnf stream (keep the slow ICNF work off the hot queue).</summary>
    private const string IcnfKickoffStream = "icnf";
}
