using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>Read queries for ignition clusters (the <c>ignitionClusters</c> query and <c>incident.clusterId</c>).</summary>
public sealed class IgnitionClusterReads(MongoContext context)
{
    private static readonly SortDefinition<IgnitionCluster> NewestFirst =
        Builders<IgnitionCluster>.Sort.Descending(x => x.LastAt);

    /// <summary>All clusters (optionally active-only), newest activity first.</summary>
    public async Task<IReadOnlyList<IgnitionCluster>> ListAsync(bool activeOnly, CancellationToken ct = default)
    {
        var filter = activeOnly
            ? Builders<IgnitionCluster>.Filter.Eq(x => x.Active, true)
            : Builders<IgnitionCluster>.Filter.Empty;
        return await context.IgnitionClusters.Find(filter).Sort(NewestFirst).ToListAsync(ct);
    }

    /// <summary>Maps each of the given incident ids to the id of the active cluster containing it (if any).</summary>
    public async Task<IReadOnlyDictionary<string, string>> ActiveClusterIdByIncidentAsync(IReadOnlyList<string> incidentIds, CancellationToken ct = default)
    {
        var f = Builders<IgnitionCluster>.Filter;
        var clusters = await context.IgnitionClusters
            .Find(f.Eq(x => x.Active, true) & f.AnyIn(x => x.IncidentIds, incidentIds))
            .ToListAsync(ct);

        var wanted = incidentIds.ToHashSet();
        var map = new Dictionary<string, string>();
        foreach (var cluster in clusters)
            foreach (var id in cluster.IncidentIds)
                if (wanted.Contains(id))
                    map.TryAdd(id, cluster.Id);
        return map;
    }
}
