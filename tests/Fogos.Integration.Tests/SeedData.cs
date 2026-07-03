using System.Security.Cryptography;
using System.Text;
using Fogos.Domain.Auth;
using Fogos.Domain.Geo;
using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Weather;
using Fogos.Infrastructure.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.Integration.Tests;

/// <summary>Fixture-scoped seeding helpers over the real <see cref="MongoContext"/>.</summary>
public static class SeedData
{
    public static MongoContext Context(ContainerFixture fixture) =>
        fixture.Factory.Services.GetRequiredService<MongoContext>();

    /// <summary>Wipe the collections used by the read tests so each test starts clean.</summary>
    public static async Task ResetAsync(ContainerFixture fixture)
    {
        var ctx = Context(fixture);
        await ctx.Incidents.DeleteManyAsync(FilterDefinition<Incident>.Empty);
        await ctx.IncidentPhotos.DeleteManyAsync(FilterDefinition<IncidentPhoto>.Empty);
        await ctx.Hotspots.DeleteManyAsync(FilterDefinition<Hotspots>.Empty);
        await ctx.WeatherStations.DeleteManyAsync(FilterDefinition<WeatherStation>.Empty);
        await ctx.WeatherHourly.DeleteManyAsync(FilterDefinition<WeatherObservation>.Empty);
        await ctx.ApiClients.DeleteManyAsync(FilterDefinition<ApiClient>.Empty);
    }

    /// <summary>The SHA-256 hex hash used for API-key lookup (mirrors <c>ApiKeyResolver.Hash</c>).</summary>
    public static string HashKey(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    /// <summary>Inserts an <see cref="ApiClient"/> for <paramref name="plaintext"/> and returns its id.</summary>
    public static async Task<string> InsertApiKeyAsync(
        ContainerFixture fixture,
        string plaintext,
        ApiTier tier,
        string name = "test key",
        IEnumerable<string>? scopes = null,
        bool publicContext = false,
        IEnumerable<string>? allowedOrigins = null,
        bool revoked = false)
    {
        var ctx = Context(fixture);
        var client = new ApiClient
        {
            Name = name,
            KeyHash = HashKey(plaintext),
            Tier = tier,
            Scopes = scopes?.ToList() ?? [],
            PublicContext = publicContext,
            AllowedOrigins = allowedOrigins?.ToList() ?? [],
            CreatedAt = DateTimeOffset.UtcNow,
            RevokedAt = revoked ? DateTimeOffset.UtcNow : null,
        };
        await ctx.ApiClients.InsertOneAsync(client);
        return client.Id;
    }

    public static Incident Incident(
        string id,
        IncidentKind kind = IncidentKind.Fire,
        bool active = true,
        DateTimeOffset? occurredAt = null,
        string concelho = "Lisboa",
        string district = "Lisboa",
        int statusCode = IncidentStatusCatalog.EmCurso,
        GeoPoint? coordinates = null,
        int? nearestStation = null,
        string naturezaCode = "3101") => new()
    {
        Id = id,
        OccurredAt = occurredAt ?? new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(1)),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        Location = "Rua de Teste",
        Concelho = concelho,
        District = district,
        Dico = "1106",
        Status = IncidentStatusCatalog.FromCode(statusCode),
        Kind = kind,
        NaturezaCode = naturezaCode,
        Natureza = "Incêndio",
        Resources = new Resources { Man = 10, Terrain = 3, Aerial = 1 },
        Active = active,
        Coordinates = coordinates ?? GeoPoint.FromLatLng(38.72, -9.14),
        NearestWeatherStationId = nearestStation,
    };

    public static IncidentPhoto Photo(string incidentId, ModerationStatus status = ModerationStatus.Approved, bool @public = true) => new()
    {
        IncidentId = incidentId,
        Status = status,
        Public = @public,
        StorageKey = $"photos/{incidentId}/{Guid.NewGuid():N}.jpg",
        Width = 1024,
        Height = 768,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public static WeatherStation Station(int id = 1) => new()
    {
        Id = id,
        Coordinates = GeoPoint.FromLatLng(38.77, -9.13),
        Name = "Lisboa (Geofísico)",
        Place = "Lisboa",
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public static WeatherObservation Observation(int stationId = 1) => new()
    {
        StationId = stationId,
        At = DateTimeOffset.UtcNow,
        Temperature = 28.5,
        Humidity = 40,
        WindSpeedKmh = 12,
        WindDirection = "N",
    };

    public static Hotspots HotspotsDoc(string incidentId, IEnumerable<GeoPoint> viirs) => new()
    {
        IncidentId = incidentId,
        Viirs = viirs.Select(p => new HotspotSample(p, DateTimeOffset.UtcNow, 320.5, "high")).ToList(),
        Modis = [],
        FetchedAt = DateTimeOffset.UtcNow,
    };
}
