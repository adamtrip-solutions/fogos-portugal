using System.Globalization;
using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Ingest;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Scheduling;
using Fogos.Infrastructure.Sources;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Icnf;

/// <summary>
/// Ports <c>ProcessICNFNewFireData</c> (every 5 min): parse the ICNF occurrences table, and for each
/// unseen occurrence fetch its XML and create a hardcoded-natureza "3103" (Mato) fire with <c>-1</c>
/// resource sentinels (the ICNF-only marker — no ANEPC means data). Creation runs through
/// <see cref="IncidentIngestService"/> so the same events fire. For incidents we already track,
/// only ICNF-only ones get their status bumped forward (mirrors the legacy guard).
/// </summary>
public sealed class ProcessIcnfNewFireDataJob(
    ISingleFlightLock lockService,
    ILogger<ProcessIcnfNewFireDataJob> logger,
    IcnfClient client,
    IncidentIngestService ingest,
    MongoContext mongo,
    IEventDispatcher dispatcher,
    IClock clock,
    IOpsNotifier ops) : UniqueJob(lockService, logger)
{
    private static readonly string[] DateFormats =
        ["dd-MM-yyyy HH:mm", "dd/MM/yyyy HH:mm", "yyyy-MM-dd HH:mm", "dd-MM-yyyy H:mm", "yyyy-MM-dd HH:mm:ss"];

    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        string html;
        try
        {
            html = await client.FetchTableAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ICNF table fetch failed.");
            await ops.ErrorAsync($"ICNF faztable fetch failed: {ex.Message}", ct);
            return;
        }

        var rows = IcnfTableParser.Parse(html);
        var created = 0;

        foreach (var row in rows)
        {
            if (ct.IsCancellationRequested)
                break;

            var existing = await mongo.Incidents
                .Find(Builders<Incident>.Filter.Eq(x => x.Id, row.Id))
                .FirstOrDefaultAsync(ct);

            if (existing is null)
            {
                if (await CreateFromIcnfAsync(row, ct))
                    created++;
            }
            else
            {
                await MaybeBumpStatusAsync(existing, row, ct);
            }
        }

        logger.LogInformation("ICNF new-fire: {Created} incidents created from {Rows} table rows.", created, rows.Count);
    }

    private async Task<bool> CreateFromIcnfAsync(IcnfTableRow row, CancellationToken ct)
    {
        IcnfOccurrence? occ;
        try
        {
            occ = IcnfOccurrenceXml.Parse(await client.FetchOccurrenceXmlAsync(row.Id, ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ICNF occurrence XML fetch failed for {Id}.", row.Id);
            return false;
        }

        if (occ is null)
            return false;

        var raw = new RawIncident
        {
            Id = row.Id,
            OccurredAt = ParseWhen(occ.DataAlerta, occ.HoraAlerta),
            NaturezaCode = "3103",
            Natureza = "Mato",
            StatusLabel = row.StatusLabel,
            Concelho = occ.Concelho ?? "",
            Freguesia = occ.Freguesia,
            Localidade = occ.Local,
            PreResolvedDistrict = occ.Distrito,
            PreResolvedDico = occ.Ine,
            Lat = occ.Lat,
            Lng = occ.Lon,
            // -1 sentinels mark an ICNF-only incident (no ANEPC means data). Kept as-is (legacy contract).
            Resources = new Resources { Man = -1, Terrain = -1, Aerial = -1, Aquatic = -1 },
        };

        var outcome = await ingest.IngestAsync([raw], ct);
        return outcome.Created > 0;
    }

    private async Task MaybeBumpStatusAsync(Incident existing, IcnfTableRow row, CancellationToken ct)
    {
        var res = existing.Resources;
        var icnfOnly = res.Man == -1 && res.Terrain == -1 && res.Aerial == -1;
        if (!icnfOnly)
            return; // ANEPC is the source of truth once it has real data

        if (!IncidentStatusCatalog.TryNormalize(row.StatusLabel, out var next))
            return;
        if (next.Code <= existing.Status.Code)
            return; // only ever move forward

        var previous = existing.Status;
        await mongo.Incidents.UpdateOneAsync(
            Builders<Incident>.Filter.Eq(x => x.Id, existing.Id),
            Builders<Incident>.Update
                .Set(x => x.Status, next)
                .Set(x => x.Active, IncidentStatusCatalog.IsActive(next.Code))
                .Set(x => x.UpdatedAt, clock.UtcNow),
            cancellationToken: ct);

        await dispatcher.DispatchAsync(
            new IncidentStatusChanged(existing.Id, previous.Code, previous.Label, next.Code, next.Label), ct: ct);
    }

    private DateTimeOffset ParseWhen(string? date, string? hora)
    {
        var raw = $"{date} {hora}".Trim();
        if (DateTime.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return clock.FromLisbon(exact);
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose))
            return clock.FromLisbon(loose);
        return clock.UtcNow;
    }
}
