namespace Fogos.Domain.Weather;

/// <summary>
/// Daily IPMA aggregates (legacy `weatherDataDaily`). Upsert key (StationId, Date).
/// -99 sentinels normalized to null here too — fixing the legacy daily-path gap.
/// </summary>
public sealed class DailyWeather
{
    public string Id { get; set; } = "";
    public required int StationId { get; set; }
    public required DateOnly Date { get; set; }

    public double? TempMax { get; set; }
    public double? TempMin { get; set; }
    public double? TempMean { get; set; }
    public double? PrecipitationMm { get; set; }
}
