using Fogos.Domain.Aircraft;
using Fogos.Domain.Geo;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Scheduling;
using Fogos.Integration.Tests.Incidents;
using Fogos.Worker.Jobs.Planes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Planes;

/// <summary>
/// End-to-end aircraft↔incident association: a loitering aircraft is linked to the fire it circles,
/// stale links are deactivated, and an aircraft near two fires is attached to the nearest only.
/// Driven by constructing <see cref="AircraftAssociationJob"/> and calling <c>RunAsync</c>.
/// </summary>
[Collection("fogos")]
public sealed class AircraftAssociationJobTests(ContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 15, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Links_loitering_aircraft_to_the_fire_it_circles()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        var fire = SeedData.Incident("AC_FIRE", coordinates: GeoPoint.FromLatLng(40.0, -8.0));
        await ctx.Incidents.InsertOneAsync(fire);
        await InsertPositionsAsync(ctx, "4ca7b1", GeoPoint.FromLatLng(40.005, -8.0),
            Now.AddMinutes(-10), Now.AddMinutes(-4), Now.AddMinutes(-1));

        await RunAsync();

        var link = await ctx.IncidentAircraft
            .Find(Builders<IncidentAircraftLink>.Filter.Eq(x => x.IncidentId, "AC_FIRE")).FirstOrDefaultAsync();
        Assert.NotNull(link);
        Assert.Equal("4ca7b1", link!.Icao);
        Assert.True(link.Active);
        Assert.Equal(Now, link.LastSeenAt);
        Assert.Equal(Now, link.FirstSeenAt);
        Assert.True(link.Samples >= 1);
    }

    [SkippableFact]
    public async Task Aircraft_with_too_few_samples_is_not_linked()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.Incidents.InsertOneAsync(SeedData.Incident("AC_ONE", coordinates: GeoPoint.FromLatLng(40.0, -8.0)));
        await InsertPositionsAsync(ctx, "4ca7b2", GeoPoint.FromLatLng(40.0, -8.0), Now.AddMinutes(-2));

        await RunAsync();

        var count = await ctx.IncidentAircraft.CountDocumentsAsync(FilterDefinition<IncidentAircraftLink>.Empty);
        Assert.Equal(0, count);
    }

    [SkippableFact]
    public async Task Expires_links_unseen_past_the_stale_window()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.Incidents.InsertOneAsync(SeedData.Incident("AC_STALE", coordinates: GeoPoint.FromLatLng(40.0, -8.0)));
        // Pre-existing active link last seen 30 min ago, no fresh positions this run → expires.
        await ctx.IncidentAircraft.InsertOneAsync(new IncidentAircraftLink
        {
            IncidentId = "AC_STALE", Icao = "4ca7b3",
            FirstSeenAt = Now.AddMinutes(-60), LastSeenAt = Now.AddMinutes(-30), Samples = 5, Active = true,
        });

        await RunAsync();

        var link = await ctx.IncidentAircraft
            .Find(Builders<IncidentAircraftLink>.Filter.Eq(x => x.IncidentId, "AC_STALE")).FirstAsync();
        Assert.False(link.Active);
    }

    [SkippableFact]
    public async Task Attaches_aircraft_to_the_nearest_of_multiple_fires()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.Incidents.InsertOneAsync(SeedData.Incident("AC_FAR", coordinates: GeoPoint.FromLatLng(40.00, -8.0)));
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("AC_NEAR", coordinates: GeoPoint.FromLatLng(40.05, -8.0)));
        // Aircraft at 40.04: ~4.4 km from AC_FAR, ~1.1 km from AC_NEAR (both inside 7 km).
        await InsertPositionsAsync(ctx, "4ca7b4", GeoPoint.FromLatLng(40.04, -8.0),
            Now.AddMinutes(-8), Now.AddMinutes(-3), Now.AddMinutes(-1));

        await RunAsync();

        var links = await ctx.IncidentAircraft
            .Find(Builders<IncidentAircraftLink>.Filter.Eq(x => x.Icao, "4ca7b4")).ToListAsync();
        var link = Assert.Single(links);
        Assert.Equal("AC_NEAR", link.IncidentId);
    }

    [SkippableFact]
    public async Task Transiting_aircraft_with_one_near_fix_is_not_linked()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.Incidents.InsertOneAsync(SeedData.Incident("AC_TRANSIT", coordinates: GeoPoint.FromLatLng(40.0, -8.0)));
        // Three window samples but only the latest is within 7 km — a fly-through, not loitering.
        await InsertPositionsAtAsync(ctx, "4ca7b5",
            (GeoPoint.FromLatLng(41.0, -8.0), Now.AddMinutes(-10)), // ~111 km
            (GeoPoint.FromLatLng(40.5, -8.0), Now.AddMinutes(-4)),  // ~56 km
            (GeoPoint.FromLatLng(40.005, -8.0), Now.AddMinutes(-1))); // ~0.55 km (latest)

        await RunAsync();

        var count = await ctx.IncidentAircraft.CountDocumentsAsync(FilterDefinition<IncidentAircraftLink>.Empty);
        Assert.Equal(0, count);
    }

    [SkippableFact]
    public async Task Reassignment_deactivates_the_previous_incident_link_same_pass()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.Incidents.InsertOneAsync(SeedData.Incident("AC_A", coordinates: GeoPoint.FromLatLng(40.0, -8.0)));
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("AC_B", coordinates: GeoPoint.FromLatLng(41.0, -8.0)));

        // A still-fresh active link to AC_A (would survive the stale sweep on its own).
        await ctx.IncidentAircraft.InsertOneAsync(new IncidentAircraftLink
        {
            IncidentId = "AC_A", Icao = "4ca7b6",
            FirstSeenAt = Now.AddMinutes(-30), LastSeenAt = Now, Samples = 5, Active = true,
        });

        // This pass the aircraft is loitering over AC_B (far from AC_A).
        await InsertPositionsAsync(ctx, "4ca7b6", GeoPoint.FromLatLng(41.005, -8.0),
            Now.AddMinutes(-8), Now.AddMinutes(-3), Now.AddMinutes(-1));

        await RunAsync();

        var linkA = await ctx.IncidentAircraft
            .Find(Builders<IncidentAircraftLink>.Filter.Eq(x => x.IncidentId, "AC_A")).FirstAsync();
        var linkB = await ctx.IncidentAircraft
            .Find(Builders<IncidentAircraftLink>.Filter.Eq(x => x.IncidentId, "AC_B")).FirstAsync();
        Assert.False(linkA.Active); // reassigned away immediately, not left for the stale sweep
        Assert.True(linkB.Active);
    }

    private async Task InsertPositionsAtAsync(MongoContext ctx, string icao, params (GeoPoint Position, DateTimeOffset At)[] samples)
    {
        var docs = samples.Select(s => new FlightPosition
        {
            Icao = icao, Registration = "CS-XYZ", Position = s.Position, SampledAt = s.At, Source = "test",
        });
        await ctx.FlightPositions.InsertManyAsync(docs);
    }

    private async Task InsertPositionsAsync(MongoContext ctx, string icao, GeoPoint position, params DateTimeOffset[] sampledAt)
    {
        var docs = sampledAt.Select(at => new FlightPosition
        {
            Icao = icao, Registration = "CS-XYZ", Position = position, SampledAt = at, Source = "test",
        });
        await ctx.FlightPositions.InsertManyAsync(docs);
    }

    private async Task ResetAsync()
    {
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.IncidentAircraft.DeleteManyAsync(FilterDefinition<IncidentAircraftLink>.Empty);
        await ctx.FlightPositions.DeleteManyAsync(FilterDefinition<FlightPosition>.Empty);
    }

    private async Task RunAsync()
    {
        var services = fixture.Factory.Services;
        var mongo = services.GetRequiredService<MongoContext>();
        var redis = services.GetRequiredService<IConnectionMultiplexer>();
        var job = new AircraftAssociationJob(
            new RedisSingleFlightLock(redis), NullLogger<AircraftAssociationJob>.Instance, mongo,
            services.GetRequiredService<IncidentReads>(),
            new TestClock { UtcNow = Now },
            Options.Create(new AircraftAssociationOptions()));
        await job.RunAsync(CancellationToken.None);
    }
}
