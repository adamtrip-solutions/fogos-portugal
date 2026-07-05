using Fogos.Domain.Aircraft;
using Fogos.Domain.Geo;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Planes;

/// <summary>
/// The read schema surfaces the aircraft associated with an incident (<c>incident.aircraft</c>, joined
/// to the tracked-fleet metadata) and the active incident an aircraft is currently attached to
/// (<c>aircraft.currentIncidentId</c>).
/// </summary>
[Collection("fogos")]
public sealed class AircraftAssociationGraphQLTests(ContainerFixture fixture)
{
    private async Task ResetAsync()
    {
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.IncidentAircraft.DeleteManyAsync(FilterDefinition<IncidentAircraftLink>.Empty);
        await ctx.TrackedAircraft.DeleteManyAsync(FilterDefinition<TrackedAircraft>.Empty);
        await ctx.FlightPositions.DeleteManyAsync(FilterDefinition<FlightPosition>.Empty);
    }

    [SkippableFact]
    public async Task Incident_aircraft_joins_tracked_metadata()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.Incidents.InsertOneAsync(SeedData.Incident("GQL_AC"));
        await ctx.TrackedAircraft.InsertOneAsync(new TrackedAircraft
        {
            Icao = "4ca7b1", Registration = "CS-ABC", Name = "Canadair", Kind = "plane",
        });
        await ctx.IncidentAircraft.InsertOneAsync(new IncidentAircraftLink
        {
            IncidentId = "GQL_AC", Icao = "4ca7b1",
            FirstSeenAt = DateTimeOffset.UtcNow.AddMinutes(-10), LastSeenAt = DateTimeOffset.UtcNow,
            Samples = 4, Active = true,
        });

        using var doc = await fixture.GraphQLAsync(
            "query($id: ID!){ incident(id:$id){ aircraft { icao registration name kind active samples } } }",
            new { id = "GQL_AC" });
        var list = doc.RootElement.GetProperty("data").GetProperty("incident").GetProperty("aircraft");
        var ac = Assert.Single(list.EnumerateArray());
        Assert.Equal("4ca7b1", ac.GetProperty("icao").GetString());
        Assert.Equal("CS-ABC", ac.GetProperty("registration").GetString());
        Assert.Equal("plane", ac.GetProperty("kind").GetString());
        Assert.True(ac.GetProperty("active").GetBoolean());
        Assert.Equal(4, ac.GetProperty("samples").GetInt32());
    }

    [SkippableFact]
    public async Task Aircraft_current_incident_id_reports_active_link()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.TrackedAircraft.InsertOneAsync(new TrackedAircraft { Icao = "4ca7b2", Registration = "CS-DEF" });
        await ctx.FlightPositions.InsertOneAsync(new FlightPosition
        {
            Icao = "4ca7b2", Registration = "CS-DEF", Position = GeoPoint.FromLatLng(40.0, -8.0),
            SampledAt = DateTimeOffset.UtcNow, Source = "test",
        });
        await ctx.IncidentAircraft.InsertOneAsync(new IncidentAircraftLink
        {
            IncidentId = "FIRE_X", Icao = "4ca7b2",
            FirstSeenAt = DateTimeOffset.UtcNow.AddMinutes(-5), LastSeenAt = DateTimeOffset.UtcNow,
            Samples = 2, Active = true,
        });

        using var doc = await fixture.GraphQLAsync("query { aircraft { tracked { icao } currentIncidentId } }");
        var list = doc.RootElement.GetProperty("data").GetProperty("aircraft");
        var ac = list.EnumerateArray().Single(a => a.GetProperty("tracked").GetProperty("icao").GetString() == "4ca7b2");
        Assert.Equal("FIRE_X", ac.GetProperty("currentIncidentId").GetString());
    }
}
