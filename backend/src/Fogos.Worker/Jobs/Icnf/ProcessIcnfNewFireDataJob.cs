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
    IProcessedMarker marker,
    Microsoft.Extensions.Options.IOptions<FogosSourcesOptions> sources,
    IClock clock,
    IOpsNotifier ops) : UniqueJob(lockService, logger)
{
    /// <summary>Redis key prefix for occurrences ruled too old to ingest — never re-fetched.</summary>
    private const string TooOldKeyPrefix = "icnf:too-old:";

    private static readonly string[] DateFormats =
        ["dd-MM-yyyy HH:mm", "dd/MM/yyyy HH:mm", "yyyy-MM-dd HH:mm", "dd-MM-yyyy H:mm", "yyyy-MM-dd HH:mm:ss", "dd-MM-yyyy HH:mm:ss"];

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
        var opts = sources.Value.Icnf;
        var created = 0;
        var tooOld = 0;
        var fetches = 0;
        var deferred = 0;

        foreach (var row in rows)
        {
            if (ct.IsCancellationRequested)
                break;

            var existing = await mongo.Incidents
                .Find(Builders<Incident>.Filter.Eq(x => x.Id, row.Id))
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
            {
                await MaybeBumpStatusAsync(existing, row, ct);
                continue;
            }

            // The faztable lists the entire season; the whole-run flood guards live here.
            // Cheapest first: the table row's own DHInicio timestamp rules out old occurrences
            // without spending an XML fetch — a season's backlog dies in one run.
            if (TryParseLisbon(row.StartedAtRaw, out var rowStartedAt)
                && rowStartedAt < clock.UtcNow.AddDays(-opts.NewFireLookbackDays))
            {
                if (await marker.TryMarkAsync(TooOldKeyPrefix + row.Id, ct))
                    tooOld++;
                continue;
            }

            // TryMarkAsync claims the key, so a row is only ruled out after a fetch proved it old.
            if (!await marker.TryMarkAsync(TooOldKeyPrefix + row.Id, ct))
                continue; // already ruled too old on a previous run

            if (fetches >= opts.MaxOccurrenceFetchesPerRun)
            {
                await marker.UnmarkAsync(TooOldKeyPrefix + row.Id, ct); // not judged yet — retry next run
                deferred++;
                continue;
            }

            fetches++;
            switch (await CreateFromIcnfAsync(row, opts.NewFireLookbackDays, ct))
            {
                case CreateOutcome.Created:
                    await marker.UnmarkAsync(TooOldKeyPrefix + row.Id, ct); // it exists now; key is noise
                    created++;
                    break;
                case CreateOutcome.TooOld:
                    tooOld++; // keep the marker: never fetch this occurrence again
                    break;
                case CreateOutcome.Failed:
                    await marker.UnmarkAsync(TooOldKeyPrefix + row.Id, ct); // transient — retry next run
                    break;
            }
        }

        if (deferred > 0)
            logger.LogWarning(
                "ICNF new-fire: fetch cap ({Cap}) reached; {Deferred} occurrences deferred to the next run.",
                opts.MaxOccurrenceFetchesPerRun, deferred);
        logger.LogInformation(
            "ICNF new-fire: {Created} created, {TooOld} too old, {Deferred} deferred ({Rows} table rows).",
            created, tooOld, deferred, rows.Count);
    }

    private enum CreateOutcome { Created, TooOld, Failed }

    private async Task<CreateOutcome> CreateFromIcnfAsync(IcnfTableRow row, int lookbackDays, CancellationToken ct)
    {
        IcnfOccurrence? occ;
        try
        {
            occ = IcnfOccurrenceXml.Parse(await client.FetchOccurrenceXmlAsync(row.Id, ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ICNF occurrence XML fetch failed for {Id}.", row.Id);
            return CreateOutcome.Failed;
        }

        if (occ is null)
            return CreateOutcome.Failed;

        var occurredAt = ParseWhen(occ.DataAlerta, occ.HoraAlerta);
        if (occurredAt < clock.UtcNow.AddDays(-lookbackDays))
            return CreateOutcome.TooOld;

        var raw = new RawIncident
        {
            Id = row.Id,
            OccurredAt = occurredAt,
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
        return outcome.Created > 0 ? CreateOutcome.Created : CreateOutcome.Failed;
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

    /// <summary>Strict parse of a Lisbon-local feed timestamp; false = unknown, caller falls back to the XML.</summary>
    private bool TryParseLisbon(string raw, out DateTimeOffset at)
    {
        at = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        if (!DateTime.TryParseExact(raw.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return false;
        at = clock.FromLisbon(exact);
        return true;
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
