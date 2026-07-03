using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Fogos.Integration.Tests;

[Collection("fogos")]
public sealed class SchemaTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Schema_exposes_the_expected_query_and_subscription_surface()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        var resolver = fixture.Factory.Services.GetRequiredService<IRequestExecutorResolver>();
        var executor = await resolver.GetRequestExecutorAsync();
        var sdl = executor.Schema.ToString();

        // Query surface.
        Assert.Contains("type Query", sdl);
        Assert.Contains("incident(id: ID!): Incident", sdl);
        Assert.Contains("incidents(filter: IncidentFilter", sdl);
        Assert.Contains("activeIncidents(kind: [IncidentKind!]): [Incident!]!", sdl);
        Assert.Contains("stats: Stats!", sdl);
        Assert.Contains("fireRisk(day: RiskDay!", sdl);
        Assert.Contains("aircraftTrack(icao: String!", sdl);

        // Input + key types.
        Assert.Contains("input IncidentFilter", sdl);
        Assert.Contains("type GeoPoint", sdl);
        Assert.Contains("type WeatherObservation", sdl);
        Assert.Contains("type AircraftPosition", sdl);

        // Subscription surface.
        Assert.Contains("type Subscription", sdl);
        Assert.Contains("incidentUpdated(id: ID): Incident!", sdl);
        Assert.Contains("activeIncidentsChanged: ActiveIncidentsDelta!", sdl);
        Assert.Contains("warningAdded: Warning!", sdl);
    }
}
