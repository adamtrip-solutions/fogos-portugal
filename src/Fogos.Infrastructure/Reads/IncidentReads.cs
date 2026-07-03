using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>
/// Thin, driver-direct read queries for incidents and their per-incident satellites
/// (history, status history, public photos, hotspots). No generic repository — every
/// method is exactly a query a resolver or DataLoader needs.
/// </summary>
public sealed class IncidentReads(MongoContext context)
{
    /// <summary>The single list ordering: newest occurrence first, id as a stable tiebreak.</summary>
    public static readonly SortDefinition<Incident> StandardSort =
        Builders<Incident>.Sort.Descending(x => x.OccurredAt).Descending(x => x.Id);

    public async Task<Incident?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await context.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<string, Incident>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        var items = await context.Incidents
            .Find(Builders<Incident>.Filter.In(x => x.Id, ids))
            .ToListAsync(ct);
        return items.ToDictionary(x => x.Id);
    }

    /// <summary>Runs a hand-built filter under the standard sort with a hard row cap.</summary>
    public async Task<List<Incident>> FindAsync(FilterDefinition<Incident> filter, int limit, CancellationToken ct = default) =>
        await context.Incidents.Find(filter).Sort(StandardSort).Limit(limit).ToListAsync(ct);

    public async Task<IReadOnlyList<Incident>> ActiveAsync(IReadOnlyList<IncidentKind> kinds, CancellationToken ct = default)
    {
        var filter = Builders<Incident>.Filter.Eq(x => x.Active, true);
        if (kinds is { Count: > 0 })
            filter &= Builders<Incident>.Filter.In(x => x.Kind, kinds);
        return await context.Incidents.Find(filter).Sort(StandardSort).ToListAsync(ct);
    }

    // ── Per-incident satellites (batched for DataLoaders) ──────────────────────

    public async Task<List<IncidentHistorySnapshot>> HistoryByIncidentsAsync(IReadOnlyList<string> incidentIds, CancellationToken ct = default) =>
        await context.IncidentHistory
            .Find(Builders<IncidentHistorySnapshot>.Filter.In(x => x.IncidentId, incidentIds))
            .Sort(Builders<IncidentHistorySnapshot>.Sort.Descending(x => x.At))
            .ToListAsync(ct);

    public async Task<List<IncidentStatusChange>> StatusHistoryByIncidentsAsync(IReadOnlyList<string> incidentIds, CancellationToken ct = default) =>
        await context.IncidentStatusHistory
            .Find(Builders<IncidentStatusChange>.Filter.In(x => x.IncidentId, incidentIds))
            .Sort(Builders<IncidentStatusChange>.Sort.Descending(x => x.At))
            .ToListAsync(ct);

    /// <summary>Approved AND public only — moderation is never bypassed by the read API.</summary>
    public async Task<List<IncidentPhoto>> PublicPhotosByIncidentsAsync(IReadOnlyList<string> incidentIds, CancellationToken ct = default)
    {
        var f = Builders<IncidentPhoto>.Filter;
        var filter = f.In(x => x.IncidentId, incidentIds)
                     & f.Eq(x => x.Status, ModerationStatus.Approved)
                     & f.Eq(x => x.Public, true);
        return await context.IncidentPhotos
            .Find(filter)
            .Sort(Builders<IncidentPhoto>.Sort.Descending(x => x.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, Hotspots>> HotspotsByIdsAsync(IReadOnlyList<string> incidentIds, CancellationToken ct = default)
    {
        var items = await context.Hotspots
            .Find(Builders<Hotspots>.Filter.In(x => x.IncidentId, incidentIds))
            .ToListAsync(ct);
        return items.ToDictionary(x => x.IncidentId);
    }
}
