using Fogos.Domain.Events;
using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Queue;
using Fogos.Integration.Tests.Incidents;
using Fogos.Worker.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Signals;

/// <summary>
/// The two rekindle triggers: status regression (7/8/9 → 5) flags STATUS_REGRESSION idempotently; a new
/// fire near a recently closed one flags PROXIMITY referencing the prior incident.
/// </summary>
[Collection("fogos")]
public sealed class RekindleHandlerTests(ContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 15, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Status_regression_flags_rekindle_and_dispatches_once()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.Incidents.InsertOneAsync(SeedData.Incident("REK_REG"));

        var (handler, redis) = BuildHandler();
        var regression = new IncidentStatusChanged("REK_REG", 7, "Em Resolução", 5, "Em Curso");
        await handler.HandleAsync(regression, CancellationToken.None);
        await handler.HandleAsync(regression, CancellationToken.None); // redelivery — must be a no-op

        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "REK_REG")).FirstAsync();
        Assert.True(stored.Signals!.Rekindle);
        Assert.Null(stored.Signals.RekindleOfId);
        Assert.NotNull(stored.Signals.RekindleDetectedAt);

        var events = await SignalsTestSupport.ReadEventsAsync(redis);
        var rekindle = Assert.Single(events.OfType<RekindleDetected>(), e => e.IncidentId == "REK_REG");
        Assert.Equal(RekindleDetected.StatusRegression, rekindle.Kind);
        Assert.Null(rekindle.PriorIncidentId);
    }

    [SkippableFact]
    public async Task Non_regression_transition_does_not_flag()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.Incidents.InsertOneAsync(SeedData.Incident("REK_FWD"));

        var (handler, redis) = BuildHandler();
        await handler.HandleAsync(new IncidentStatusChanged("REK_FWD", 5, "Em Curso", 6, "Chegada ao TO"), CancellationToken.None);

        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "REK_FWD")).FirstAsync();
        Assert.True(stored.Signals is null || !stored.Signals.Rekindle);
        var events = await SignalsTestSupport.ReadEventsAsync(redis);
        Assert.DoesNotContain(events.OfType<RekindleDetected>(), e => e.IncidentId == "REK_FWD");
    }

    [SkippableFact]
    public async Task Proximity_to_recently_closed_fire_flags_rekindle()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);
        await EnsureGeoIndexAsync(ctx);

        // Prior fire, concluded (status 8), with a last status change 10 h ago (within 48 h).
        var prior = SeedData.Incident("REK_PRIOR", active: false,
            statusCode: IncidentStatusCatalog.Conclusao, coordinates: GeoPoint.FromLatLng(40.0, -8.0));
        await ctx.Incidents.InsertOneAsync(prior);
        await ctx.IncidentStatusHistory.InsertOneAsync(new IncidentStatusChange
        {
            IncidentId = "REK_PRIOR", At = Now.AddHours(-10), Code = IncidentStatusCatalog.Conclusao, Label = "Conclusão",
        });

        // New fire ~1.1 km away.
        var fresh = SeedData.Incident("REK_NEW", coordinates: GeoPoint.FromLatLng(40.01, -8.0));
        await ctx.Incidents.InsertOneAsync(fresh);

        var (handler, redis) = BuildHandler();
        await handler.HandleAsync(new IncidentCreated("REK_NEW"), CancellationToken.None);

        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "REK_NEW")).FirstAsync();
        Assert.True(stored.Signals!.Rekindle);
        Assert.Equal("REK_PRIOR", stored.Signals.RekindleOfId);

        var events = await SignalsTestSupport.ReadEventsAsync(redis);
        var rekindle = Assert.Single(events.OfType<RekindleDetected>(), e => e.IncidentId == "REK_NEW");
        Assert.Equal(RekindleDetected.Proximity, rekindle.Kind);
        Assert.Equal("REK_PRIOR", rekindle.PriorIncidentId);
    }

    [SkippableFact]
    public async Task No_nearby_closed_fire_leaves_incident_unflagged()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);
        await EnsureGeoIndexAsync(ctx);

        var fresh = SeedData.Incident("REK_ALONE", coordinates: GeoPoint.FromLatLng(41.5, -8.5));
        await ctx.Incidents.InsertOneAsync(fresh);

        var (handler, redis) = BuildHandler();
        await handler.HandleAsync(new IncidentCreated("REK_ALONE"), CancellationToken.None);

        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "REK_ALONE")).FirstAsync();
        Assert.True(stored.Signals is null || !stored.Signals.Rekindle);
        var events = await SignalsTestSupport.ReadEventsAsync(redis);
        Assert.DoesNotContain(events.OfType<RekindleDetected>(), e => e.IncidentId == "REK_ALONE");
    }

    [SkippableFact]
    public async Task Regression_then_proximity_both_dispatch_with_correct_payloads()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);
        await EnsureGeoIndexAsync(ctx);

        await SeedNearbyClosedPriorAsync(ctx, "REK_PRIOR_A");
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("REK_BOTH_A", coordinates: GeoPoint.FromLatLng(40.01, -8.0)));

        var (handler, redis) = BuildHandler();
        await handler.HandleAsync(new IncidentStatusChanged("REK_BOTH_A", 7, "Em Resolução", 5, "Em Curso"), CancellationToken.None);
        await handler.HandleAsync(new IncidentCreated("REK_BOTH_A"), CancellationToken.None);

        await AssertBothKindsAsync(ctx, redis, "REK_BOTH_A", "REK_PRIOR_A");
    }

    [SkippableFact]
    public async Task Proximity_then_regression_both_dispatch_and_prior_id_is_kept()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);
        await EnsureGeoIndexAsync(ctx);

        await SeedNearbyClosedPriorAsync(ctx, "REK_PRIOR_B");
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("REK_BOTH_B", coordinates: GeoPoint.FromLatLng(40.01, -8.0)));

        var (handler, redis) = BuildHandler();
        await handler.HandleAsync(new IncidentCreated("REK_BOTH_B"), CancellationToken.None);
        await handler.HandleAsync(new IncidentStatusChanged("REK_BOTH_B", 8, "Conclusão", 5, "Em Curso"), CancellationToken.None);

        // Status regression must not overwrite the proximity-set prior id.
        await AssertBothKindsAsync(ctx, redis, "REK_BOTH_B", "REK_PRIOR_B");
    }

    private async Task SeedNearbyClosedPriorAsync(MongoContext ctx, string priorId)
    {
        var prior = SeedData.Incident(priorId, active: false,
            statusCode: IncidentStatusCatalog.Conclusao, coordinates: GeoPoint.FromLatLng(40.0, -8.0));
        await ctx.Incidents.InsertOneAsync(prior);
        await ctx.IncidentStatusHistory.InsertOneAsync(new IncidentStatusChange
        {
            IncidentId = priorId, At = Now.AddHours(-10), Code = IncidentStatusCatalog.Conclusao, Label = "Conclusão",
        });
    }

    private static async Task AssertBothKindsAsync(MongoContext ctx, IConnectionMultiplexer redis, string incidentId, string priorId)
    {
        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, incidentId)).FirstAsync();
        Assert.True(stored.Signals!.Rekindle);
        Assert.Equal(priorId, stored.Signals.RekindleOfId);
        Assert.Contains(RekindleDetected.StatusRegression, stored.Signals.RekindleKinds);
        Assert.Contains(RekindleDetected.Proximity, stored.Signals.RekindleKinds);

        var events = (await SignalsTestSupport.ReadEventsAsync(redis))
            .OfType<RekindleDetected>().Where(e => e.IncidentId == incidentId).ToList();
        Assert.Equal(2, events.Count);
        var regression = Assert.Single(events, e => e.Kind == RekindleDetected.StatusRegression);
        Assert.Null(regression.PriorIncidentId);
        var proximity = Assert.Single(events, e => e.Kind == RekindleDetected.Proximity);
        Assert.Equal(priorId, proximity.PriorIncidentId);
    }

    private static async Task EnsureGeoIndexAsync(MongoContext ctx) =>
        await ctx.Incidents.Indexes.CreateOneAsync(
            new CreateIndexModel<Incident>(Builders<Incident>.IndexKeys.Geo2DSphere("coordinates")));

    private (RekindleHandler Handler, IConnectionMultiplexer Redis) BuildHandler()
    {
        var services = fixture.Factory.Services;
        var mongo = services.GetRequiredService<MongoContext>();
        var redis = services.GetRequiredService<IConnectionMultiplexer>();
        var clock = new TestClock { UtcNow = Now };
        var dispatcher = new RedisEventDispatcher(redis, clock);
        var handler = new RekindleHandler(mongo, clock, dispatcher, Options.Create(new SignalsOptions()));
        return (handler, redis);
    }
}
