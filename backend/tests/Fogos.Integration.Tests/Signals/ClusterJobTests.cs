using Fogos.Domain.Events;
using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Scheduling;
using Fogos.Integration.Tests.Incidents;
using Fogos.Worker.Jobs.Signals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Signals;

/// <summary>
/// End-to-end ignition clustering: single-linkage-groups recent fires into a cluster (dispatching
/// <see cref="ClusterDetected"/>), leaves sub-threshold groups alone, and deactivates stale clusters.
/// Driven by constructing <see cref="ClusterJob"/> and calling <c>RunAsync</c>.
/// </summary>
[Collection("fogos")]
public sealed class ClusterJobTests(ContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Groups_three_nearby_recent_fires_into_a_cluster_and_dispatches_event()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);

        // Three fires within 10 km, ignited 1 h ago (inside the 6 h window).
        await ctx.Incidents.InsertOneAsync(Fire("CL1", 40.00, -8.00, "Concelho A"));
        await ctx.Incidents.InsertOneAsync(Fire("CL2", 40.02, -8.00, "Concelho A"));
        await ctx.Incidents.InsertOneAsync(Fire("CL3", 40.00, -8.03, "Concelho B"));
        // A far fire that must not join.
        await ctx.Incidents.InsertOneAsync(Fire("FAR", 41.60, -8.00, "Concelho Z"));

        var (job, redis) = BuildJob();
        await job.RunAsync(CancellationToken.None);

        var cluster = await ctx.IgnitionClusters.Find(FilterDefinition<IgnitionCluster>.Empty).SingleAsync();
        Assert.True(cluster.Active);
        Assert.Equal(3, cluster.IncidentIds.Count);
        Assert.Equal(["CL1", "CL2", "CL3"], cluster.IncidentIds.OrderBy(x => x));
        Assert.Contains("Concelho A", cluster.Concelhos);
        Assert.Contains("Concelho B", cluster.Concelhos);

        var events = await SignalsTestSupport.ReadEventsAsync(redis);
        var detected = Assert.Single(events.OfType<ClusterDetected>());
        Assert.Equal(cluster.Id, detected.ClusterId);
        Assert.Equal(3, detected.Count);
    }

    [SkippableFact]
    public async Task Sub_threshold_group_creates_no_cluster()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.Incidents.InsertOneAsync(Fire("P1", 40.00, -8.00, "Concelho A"));
        await ctx.Incidents.InsertOneAsync(Fire("P2", 40.01, -8.00, "Concelho A"));

        var (job, _) = BuildJob();
        await job.RunAsync(CancellationToken.None);

        Assert.Equal(0, await ctx.IgnitionClusters.CountDocumentsAsync(FilterDefinition<IgnitionCluster>.Empty));
    }

    [SkippableFact]
    public async Task Stale_cluster_is_deactivated()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.IgnitionClusters.InsertOneAsync(new IgnitionCluster
        {
            IncidentIds = ["OLD1", "OLD2", "OLD3"],
            Centroid = GeoPoint.FromLatLng(39.0, -8.0),
            FirstAt = Now.AddHours(-9),
            LastAt = Now.AddHours(-7), // older than the 6 h window
            Concelhos = ["Concelho Velho"],
            Active = true,
            UpdatedAt = Now.AddHours(-7),
        });

        var (job, _) = BuildJob();
        await job.RunAsync(CancellationToken.None);

        var cluster = await ctx.IgnitionClusters.Find(FilterDefinition<IgnitionCluster>.Empty).SingleAsync();
        Assert.False(cluster.Active);
    }

    [SkippableFact]
    public async Task Bridging_fire_merges_two_clusters_leaving_one_active()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);

        // Two pre-existing active clusters, ~11 km apart (out of single-linkage range on their own).
        await ctx.IgnitionClusters.InsertOneAsync(new IgnitionCluster
        {
            IncidentIds = ["A1", "A2", "A3"], Centroid = GeoPoint.FromLatLng(40.00, -8.0),
            FirstAt = Now.AddHours(-2), LastAt = Now.AddHours(-1), Concelhos = ["Concelho A"],
            Active = true, UpdatedAt = Now.AddHours(-1),
        });
        await ctx.IgnitionClusters.InsertOneAsync(new IgnitionCluster
        {
            IncidentIds = ["B1", "B2", "B3"], Centroid = GeoPoint.FromLatLng(40.10, -8.0),
            FirstAt = Now.AddHours(-2), LastAt = Now.AddHours(-1), Concelhos = ["Concelho B"],
            Active = true, UpdatedAt = Now.AddHours(-1),
        });

        // Recent fires reproducing both clusters plus a bridge fire that links them into one group.
        await ctx.Incidents.InsertOneAsync(Fire("A1", 40.000, -8.0, "Concelho A"));
        await ctx.Incidents.InsertOneAsync(Fire("A2", 40.001, -8.0, "Concelho A"));
        await ctx.Incidents.InsertOneAsync(Fire("A3", 40.002, -8.0, "Concelho A"));
        await ctx.Incidents.InsertOneAsync(Fire("B1", 40.100, -8.0, "Concelho B"));
        await ctx.Incidents.InsertOneAsync(Fire("B2", 40.101, -8.0, "Concelho B"));
        await ctx.Incidents.InsertOneAsync(Fire("B3", 40.102, -8.0, "Concelho B"));
        await ctx.Incidents.InsertOneAsync(Fire("BRIDGE", 40.050, -8.0, "Concelho A"));

        var (job, _) = BuildJob();
        await job.RunAsync(CancellationToken.None);

        var all = await ctx.IgnitionClusters.Find(FilterDefinition<IgnitionCluster>.Empty).ToListAsync();
        var active = all.Where(c => c.Active).ToList();
        var survivor = Assert.Single(active);
        Assert.Contains("A1", survivor.IncidentIds);
        Assert.Contains("B1", survivor.IncidentIds);
        Assert.Contains("BRIDGE", survivor.IncidentIds);
        Assert.Single(all, c => !c.Active); // the other cluster was folded in and deactivated
    }

    private static Incident Fire(string id, double lat, double lng, string concelho) =>
        SeedData.Incident(id, occurredAt: Now.AddHours(-1), concelho: concelho, coordinates: GeoPoint.FromLatLng(lat, lng));

    private async Task ResetAsync()
    {
        await SeedData.ResetAsync(fixture);
        await SeedData.Context(fixture).IgnitionClusters.DeleteManyAsync(FilterDefinition<IgnitionCluster>.Empty);
    }

    private (ClusterJob Job, IConnectionMultiplexer Redis) BuildJob()
    {
        var services = fixture.Factory.Services;
        var mongo = services.GetRequiredService<MongoContext>();
        var redis = services.GetRequiredService<IConnectionMultiplexer>();
        var clock = new TestClock { UtcNow = Now };
        var lockService = new RedisSingleFlightLock(redis);
        var dispatcher = new RedisEventDispatcher(redis, clock);

        var job = new ClusterJob(
            lockService, NullLogger<ClusterJob>.Instance, mongo, clock, dispatcher,
            Options.Create(new SignalsOptions()));

        return (job, redis);
    }
}
