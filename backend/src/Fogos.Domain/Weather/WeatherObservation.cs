namespace Fogos.Domain.Weather;

/// <summary>
/// Hourly IPMA observation (legacy `weatherData`). Upsert key (StationId, At).
/// IPMA's -99 sentinels are normalized to null at ingest/import — a stored value is a real value.
/// </summary>
public sealed class WeatherObservation
{
    public string Id { get; set; } = "";
    public required int StationId { get; set; }
    public required DateTimeOffset At { get; set; }

    public double? Temperature { get; set; }
    public double? Humidity { get; set; }
    public double? WindSpeedKmh { get; set; }

    /// <summary>Cardinal direction (N/NE/E/SE/S/SW/W/NW), already decoded from IPMA's 0–9 ids.</summary>
    public string? WindDirection { get; set; }

    public double? PrecipitationMm { get; set; }
    public double? Pressure { get; set; }
    public double? Radiation { get; set; }
}

/// <summary>IPMA idDireccVento → cardinal direction (legacy WIND_DIRECTIONS, verbatim: 0 = none, 9 = N again).</summary>
public static class WindDirections
{
    private static readonly string?[] ById = [null, "N", "NE", "E", "SE", "S", "SW", "W", "NW", "N"];

    public static string? Decode(int? id) => id is >= 0 and <= 9 ? ById[id.Value] : null;
}
