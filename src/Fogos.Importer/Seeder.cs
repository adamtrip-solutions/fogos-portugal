using System.Text.Json;
using Fogos.Domain.Incidents;
using Fogos.Domain.Locations;
using Fogos.Domain.Weather;
using Fogos.Infrastructure.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.Importer;

/// <summary>Loads dev seed fixtures into MongoDB, upserting by <c>_id</c> so re-runs are idempotent.</summary>
public static class Seeder
{
    private static readonly ReplaceOptions Upsert = new() { IsUpsert = true };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new GeoPointJsonConverter() },
    };

    public static async Task<int> RunAsync(IServiceProvider services, string seedDir)
    {
        var context = services.GetRequiredService<MongoContext>();

        var incidents = await SeedIncidentsAsync(context, Path.Combine(seedDir, "incidents.json"));
        var stations = await SeedStationsAsync(context, Path.Combine(seedDir, "weather_stations.json"));
        var locations = await SeedLocationsAsync(context, Path.Combine(seedDir, "locations.json"));

        Console.WriteLine($"Seeded {incidents} incidents, {stations} weather stations, {locations} locations from '{seedDir}'.");
        return 0;
    }

    private static async Task<int> SeedIncidentsAsync(MongoContext context, string file)
    {
        var count = 0;
        foreach (var seed in Load<SeedIncident>(file))
        {
            var incident = seed.ToIncident();
            await context.Incidents.ReplaceOneAsync(
                Builders<Incident>.Filter.Eq(x => x.Id, incident.Id), incident, Upsert);
            count++;
        }
        return count;
    }

    private static async Task<int> SeedStationsAsync(MongoContext context, string file)
    {
        var count = 0;
        foreach (var station in Load<WeatherStation>(file))
        {
            await context.WeatherStations.ReplaceOneAsync(
                Builders<WeatherStation>.Filter.Eq(x => x.Id, station.Id), station, Upsert);
            count++;
        }
        return count;
    }

    private static async Task<int> SeedLocationsAsync(MongoContext context, string file)
    {
        var count = 0;
        foreach (var location in Load<Location>(file))
        {
            await context.Locations.ReplaceOneAsync(
                Builders<Location>.Filter.Eq(x => x.Id, location.Id), location, Upsert);
            count++;
        }
        return count;
    }

    private static List<T> Load<T>(string file)
    {
        if (!File.Exists(file))
            throw new FileNotFoundException($"Seed fixture not found: {file}", file);
        var json = File.ReadAllText(file);
        return JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];
    }
}
