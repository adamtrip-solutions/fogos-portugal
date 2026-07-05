namespace Fogos.Domain.Weather;

/// <summary>
/// Climate normals per station and reference period; monthly means indexed 0 = January.
/// Wave detection compares heat against 1991-2020 and cold against 1971-2000 (WMO practice
/// as the legacy platform applied it).
/// </summary>
public sealed class WeatherNormal
{
    public const string PeriodHeat = "1991-2020";
    public const string PeriodMid = "1981-2010";
    public const string PeriodCold = "1971-2000";

    public string Id { get; set; } = "";
    public required int StationId { get; set; }
    public required string Period { get; set; }

    /// <summary>12 monthly mean-maximum temperatures (January..December).</summary>
    public required double[] TmaxMean { get; set; }

    /// <summary>12 monthly mean-minimum temperatures.</summary>
    public required double[] TminMean { get; set; }
}
