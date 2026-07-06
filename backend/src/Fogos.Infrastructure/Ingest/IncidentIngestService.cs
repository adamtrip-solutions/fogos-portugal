using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Incidents;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Ingest;

/// <summary>Result of an ingest run — counts for freshness/logging plus the ids seen (for CheckIsActive).</summary>
public sealed record IngestOutcome(int Created, int Updated, int Skipped, IReadOnlySet<string> SeenIds);

/// <summary>
/// The pipeline core (ANALYSIS.md §3 step 1–2, IncidentObserver-as-events). For each
/// <see cref="RawIncident"/>: resolve location, map to canonical, diff against the stored doc, upsert
/// (preserving enrichment fields the feed doesn't own), and dispatch <see cref="IncidentCreated"/> /
/// <see cref="IncidentResourcesChanged"/> / <see cref="IncidentStatusChanged"/>. Source-agnostic:
/// ArcGIS, ANEPC, and ICNF new-fire all funnel through here so the downstream side effects are identical.
/// </summary>
public sealed class IncidentIngestService(
    MongoContext mongo,
    LocationResolver locations,
    IEventDispatcher dispatcher,
    IClock clock,
    IOpsNotifier ops,
    IncidentStatusHistoryStore statusHistory,
    ILogger<IncidentIngestService> logger)
{
    public async Task<IngestOutcome> IngestAsync(IReadOnlyList<RawIncident> raws, CancellationToken ct = default)
    {
        int created = 0, updated = 0, skipped = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in raws)
        {
            if (ct.IsCancellationRequested)
                break;
            if (string.IsNullOrEmpty(raw.Id))
            {
                skipped++;
                continue;
            }

            var location = await locations.ResolveAsync(raw, ct);
            if (location is null)
            {
                skipped++;
                continue; // ops already pinged by the resolver
            }

            var mapResult = IncidentMapper.Map(raw, location);
            if (!mapResult.Ok)
            {
                await ops.ErrorAsync($"Incident {raw.Id} rejected: {mapResult.Rejection}", ct);
                skipped++;
                continue;
            }

            var mapped = mapResult.Incident!;
            seen.Add(mapped.Id);

            var stored = await mongo.Incidents
                .Find(Builders<Incident>.Filter.Eq(x => x.Id, mapped.Id))
                .FirstOrDefaultAsync(ct);

            if (stored is null)
            {
                await InsertAsync(mapped, raw, ct); // sets LastSeenInFeedAt in the inserted doc
                created++;
            }
            else
            {
                if (await UpdateAsync(stored, mapped, ct))
                    updated++;
                // Record presence for EVERY id seen this sweep, even when the doc is otherwise unchanged,
                // so the feed-drop close-out grace window measures from the true last-seen time.
                await mongo.Incidents.UpdateOneAsync(
                    Builders<Incident>.Filter.Eq(x => x.Id, mapped.Id),
                    Builders<Incident>.Update.Set(x => x.LastSeenInFeedAt, clock.UtcNow),
                    cancellationToken: ct);
            }
        }

        logger.LogInformation("Ingest: {Created} created, {Updated} updated, {Skipped} skipped ({Total} raw).",
            created, updated, skipped, raws.Count);
        return new IngestOutcome(created, updated, skipped, seen);
    }

    private async Task InsertAsync(Incident mapped, RawIncident raw, CancellationToken ct)
    {
        var now = clock.UtcNow;
        mapped.CreatedAt = now;
        mapped.UpdatedAt = now;
        mapped.LastSeenInFeedAt = now;
        await mongo.Incidents.InsertOneAsync(mapped, cancellationToken: ct);
        // Seed the status timeline with a single observation of the initial status. This is not a
        // transition, so it is written directly (no IncidentStatusChanged) — webhooks/social must not fire.
        // ObservedStatusAt overrides the stamp for ICNF side-door fires that arrive already extinguished.
        await statusHistory.AppendAsync(mapped, raw.ObservedStatusAt, ct);
        await dispatcher.DispatchAsync(new IncidentCreated(mapped.Id), ct: ct);
    }

    private async Task<bool> UpdateAsync(Incident stored, Incident mapped, CancellationToken ct)
    {
        var change = ChangeSet.Diff(stored, mapped);
        if (change.Nothing)
            return false;

        // Carry over fields the feed does not own (enrichment + threading + assignment state).
        mapped.CreatedAt = stored.CreatedAt;
        mapped.UpdatedAt = clock.UtcNow;
        mapped.Important = stored.Important;
        mapped.Icnf = stored.Icnf;
        mapped.Kml = stored.Kml;
        mapped.KmlVost = stored.KmlVost;
        mapped.NearestWeatherStationId = stored.NearestWeatherStationId;
        mapped.LastSeenInFeedAt = stored.LastSeenInFeedAt; // preserved across replace; the caller re-stamps it to now
        // DetailLocation may have been enriched by ICNF (LOCAL); keep the richer value if the feed had none.
        mapped.DetailLocation ??= stored.DetailLocation;

        await mongo.Incidents.ReplaceOneAsync(Builders<Incident>.Filter.Eq(x => x.Id, mapped.Id), mapped, cancellationToken: ct);

        if (change.StatusChanged)
        {
            var prev = change.PreviousStatus!;
            await dispatcher.DispatchAsync(
                new IncidentStatusChanged(mapped.Id, prev.Code, prev.Label, mapped.Status.Code, mapped.Status.Label), ct: ct);
        }

        if (change.ResourcesChanged)
            await dispatcher.DispatchAsync(new IncidentResourcesChanged(mapped.Id, change.PreviousResources!, mapped.Resources), ct: ct);

        return true;
    }
}
