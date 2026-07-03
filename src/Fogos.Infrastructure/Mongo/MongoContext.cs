using Fogos.Domain.Aircraft;
using Fogos.Domain.Auth;
using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Domain.Locations;
using Fogos.Domain.Photos;
using Fogos.Domain.Risk;
using Fogos.Domain.Social;
using Fogos.Domain.Stats;
using Fogos.Domain.Warnings;
using Fogos.Domain.Weather;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Mongo;

/// <summary>Typed access to the project's MongoDB collections (clean schema, one collection per entity).</summary>
public sealed class MongoContext
{
    public IMongoDatabase Database { get; }

    public MongoContext(IMongoClient client, IOptions<MongoOptions> options)
    {
        Database = client.GetDatabase(options.Value.Database);
    }

    public IMongoCollection<Incident> Incidents => Database.GetCollection<Incident>("incidents");
    public IMongoCollection<IncidentHistorySnapshot> IncidentHistory => Database.GetCollection<IncidentHistorySnapshot>("incident_history");
    public IMongoCollection<IncidentStatusChange> IncidentStatusHistory => Database.GetCollection<IncidentStatusChange>("incident_status_history");
    public IMongoCollection<IncidentPhoto> IncidentPhotos => Database.GetCollection<IncidentPhoto>("incident_photos");
    public IMongoCollection<SocialThread> SocialThreads => Database.GetCollection<SocialThread>("social_threads");

    public IMongoCollection<WeatherStation> WeatherStations => Database.GetCollection<WeatherStation>("weather_stations");
    public IMongoCollection<WeatherObservation> WeatherHourly => Database.GetCollection<WeatherObservation>("weather_hourly");
    public IMongoCollection<DailyWeather> WeatherDaily => Database.GetCollection<DailyWeather>("weather_daily");
    public IMongoCollection<WeatherNormal> WeatherNormals => Database.GetCollection<WeatherNormal>("weather_normals");
    public IMongoCollection<TemperatureWave> TemperatureWaves => Database.GetCollection<TemperatureWave>("temperature_waves");
    public IMongoCollection<WeatherWarning> WeatherWarnings => Database.GetCollection<WeatherWarning>("weather_warnings");

    public IMongoCollection<ConcelhoRisk> RcmDaily => Database.GetCollection<ConcelhoRisk>("rcm_daily");
    public IMongoCollection<RiskGeoJson> RcmGeoJson => Database.GetCollection<RiskGeoJson>("rcm_geojson");

    public IMongoCollection<Warning> Warnings => Database.GetCollection<Warning>("warnings");

    public IMongoCollection<FlightPosition> FlightPositions => Database.GetCollection<FlightPosition>("flight_positions");
    public IMongoCollection<TrackedAircraft> TrackedAircraft => Database.GetCollection<TrackedAircraft>("tracked_aircraft");

    public IMongoCollection<Hotspots> Hotspots => Database.GetCollection<Hotspots>("hotspots");
    public IMongoCollection<Location> Locations => Database.GetCollection<Location>("locations");
    public IMongoCollection<ApiClient> ApiClients => Database.GetCollection<ApiClient>("api_clients");
    public IMongoCollection<HistoryTotal> HistoryTotals => Database.GetCollection<HistoryTotal>("history_totals");

    /// <summary>Rows that fit no importer mapping land here with a reason — nothing silently dropped.</summary>
    public IMongoCollection<BsonDocument> ImportQuarantine => Database.GetCollection<BsonDocument>("import_quarantine");
}
