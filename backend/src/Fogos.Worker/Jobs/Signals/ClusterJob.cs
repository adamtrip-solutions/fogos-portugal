using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Signals;

/// <summary>
/// Every 5 minutes, single-linkage-clusters the fires ignited in the last <c>ClusterWindowHours</c> hours
/// (with coordinates) using a <c>ClusterLinkKm</c> linkage distance. Any group of ≥ <c>ClusterMinSize</c>
/// becomes or updates a cluster: it is matched to an existing active cluster by shared incident ids and
/// refreshed, else inserted and announced via <see cref="ClusterDetected"/>. Clusters whose latest member
/// ignition falls outside the window are deactivated. Single-flight.
/// </summary>
public sealed class ClusterJob(
    ISingleFlightLock lockService,
    ILogger<ClusterJob> logger,
    MongoContext mongo,
    IClock clock,
    IEventDispatcher dispatcher,
    IOptions<SignalsOptions> options) : UniqueJob(lockService, logger)
{
    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var now = clock.UtcNow;
        var windowStart = now - TimeSpan.FromHours(opts.ClusterWindowHours);

        var f = Builders<Incident>.Filter;
        var recentFires = (await mongo.Incidents
                .Find(f.Eq(x => x.Kind, IncidentKind.Fire) & f.Gte(x => x.OccurredAt, windowStart))
                .ToListAsync(ct))
            .Where(i => i.Coordinates is not null)
            .ToList();

        var byId = recentFires.ToDictionary(i => i.Id);
        var points = recentFires
            .Select(i => new IgnitionClustering.Point(i.Id, i.Coordinates!.Value))
            .ToList();

        var groups = IgnitionClustering.Group(points, opts.ClusterLinkKm)
            .Where(g => g.Count >= opts.ClusterMinSize)
            .ToList();

        var activeClusters = await mongo.IgnitionClusters
            .Find(Builders<IgnitionCluster>.Filter.Eq(x => x.Active, true))
            .ToListAsync(ct);
        var claimed = new HashSet<string>();

        foreach (var group in groups)
        {
            try
            {
                await UpsertClusterAsync(group, byId, activeClusters, claimed, now, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ignition-cluster upsert failed for a group of {Count}", group.Count);
            }
        }

        // Deactivate clusters whose latest member ignition is now outside the window.
        await mongo.IgnitionClusters.UpdateManyAsync(
            Builders<IgnitionCluster>.Filter.Eq(x => x.Active, true) & Builders<IgnitionCluster>.Filter.Lt(x => x.LastAt, windowStart),
            Builders<IgnitionCluster>.Update.Set(x => x.Active, false).Set(x => x.UpdatedAt, now),
            cancellationToken: ct);
    }

    private async Task UpsertClusterAsync(
        IReadOnlyList<IgnitionClustering.Point> group,
        IReadOnlyDictionary<string, Incident> byId,
        IReadOnlyList<IgnitionCluster> activeClusters,
        HashSet<string> claimed,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var groupIds = group.Select(p => p.IncidentId).ToList();
        var members = groupIds.Select(id => byId[id]).ToList();
        var centroid = IgnitionClustering.Centroid(group);
        var firstAt = members.Min(m => m.OccurredAt);
        var lastAt = members.Max(m => m.OccurredAt);
        var concelhos = members.Select(m => m.Concelho).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();

        // Match to an existing active cluster by shared incident ids (each existing cluster claimed once).
        var groupIdSet = groupIds.ToHashSet();
        var existing = activeClusters.FirstOrDefault(c => !claimed.Contains(c.Id) && c.IncidentIds.Any(groupIdSet.Contains));

        if (existing is not null)
        {
            claimed.Add(existing.Id);
            await mongo.IgnitionClusters.UpdateOneAsync(
                Builders<IgnitionCluster>.Filter.Eq(x => x.Id, existing.Id),
                Builders<IgnitionCluster>.Update
                    .Set(x => x.IncidentIds, groupIds)
                    .Set(x => x.Centroid, centroid)
                    .Set(x => x.FirstAt, firstAt)
                    .Set(x => x.LastAt, lastAt)
                    .Set(x => x.Concelhos, concelhos)
                    .Set(x => x.Active, true)
                    .Set(x => x.UpdatedAt, now),
                cancellationToken: ct);

            // A new fire can bridge two previously-separate clusters into one group. Fold the survivors of
            // any OTHER active cluster whose members intersect this group into it and deactivate them, so a
            // merge never leaves a stale duplicate active.
            var absorbed = activeClusters
                .Where(c => c.Id != existing.Id && !claimed.Contains(c.Id) && c.IncidentIds.Any(groupIdSet.Contains))
                .Select(c => c.Id)
                .ToList();
            if (absorbed.Count > 0)
            {
                foreach (var id in absorbed)
                    claimed.Add(id);
                await mongo.IgnitionClusters.UpdateManyAsync(
                    Builders<IgnitionCluster>.Filter.In(x => x.Id, absorbed),
                    Builders<IgnitionCluster>.Update.Set(x => x.Active, false).Set(x => x.UpdatedAt, now),
                    cancellationToken: ct);
            }
            return;
        }

        var cluster = new IgnitionCluster
        {
            IncidentIds = groupIds,
            Centroid = centroid,
            FirstAt = firstAt,
            LastAt = lastAt,
            Concelhos = concelhos,
            Active = true,
            UpdatedAt = now,
        };
        await mongo.IgnitionClusters.InsertOneAsync(cluster, cancellationToken: ct);
        await dispatcher.DispatchAsync(new ClusterDetected(cluster.Id, cluster.IncidentIds.Count), ct: ct);
    }
}
