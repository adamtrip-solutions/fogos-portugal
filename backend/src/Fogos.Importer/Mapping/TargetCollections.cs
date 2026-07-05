namespace Fogos.Importer.Mapping;

/// <summary>New-schema collection names, mirroring <see cref="Fogos.Infrastructure.Mongo.MongoContext"/>.</summary>
public static class TargetCollections
{
    public const string Incidents = "incidents";
    public const string IncidentHistory = "incident_history";
    public const string IncidentStatusHistory = "incident_status_history";
    public const string IncidentPhotos = "incident_photos";

    public const string WeatherStations = "weather_stations";
    public const string WeatherHourly = "weather_hourly";
    public const string WeatherDaily = "weather_daily";
    public const string WeatherNormals = "weather_normals";
    public const string TemperatureWaves = "temperature_waves";
    public const string WeatherWarnings = "weather_warnings";

    public const string RcmDaily = "rcm_daily";
    public const string RcmGeoJson = "rcm_geojson";

    public const string Warnings = "warnings";

    public const string FlightPositions = "flight_positions";
    public const string TrackedAircraft = "tracked_aircraft";

    public const string Hotspots = "hotspots";
    public const string Locations = "locations";
    public const string HistoryTotals = "history_totals";
}
