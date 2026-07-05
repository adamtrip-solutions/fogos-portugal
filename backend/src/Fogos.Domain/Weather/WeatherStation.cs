using Fogos.Domain.Geo;

namespace Fogos.Domain.Weather;

/// <summary>IPMA observation station. `_id` = IPMA stationId.</summary>
public sealed class WeatherStation
{
    public required int Id { get; set; }
    public required GeoPoint Coordinates { get; set; }
    public string Name { get; set; } = "";
    public string? Place { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
