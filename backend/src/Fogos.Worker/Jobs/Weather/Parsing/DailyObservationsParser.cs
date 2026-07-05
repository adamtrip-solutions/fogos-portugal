using System.Text.Json;
using Fogos.Domain.Weather;

namespace Fogos.Worker.Jobs.Weather.Parsing;

/// <summary>
/// Parses IPMA daily observations: a map of <c>date → (stationId → daily aggregates)</c>.
/// Port of <c>UpdateWeatherDataDaily.php</c>, mapping temp_max/temp_min/temp_med/prec_quant.
/// Deliberate fix vs legacy: the <c>-99 → null</c> sentinel is applied here too (the legacy daily
/// path never stripped it — see ANALYSIS.md §6.5 and DailyWeather.cs).
/// </summary>
public static class DailyObservationsParser
{
    public static IReadOnlyList<DailyWeather> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var daily = new List<DailyWeather>();

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return daily;

        foreach (var dateGroup in doc.RootElement.EnumerateObject())
        {
            var date = IpmaJson.ParseDate(dateGroup.Name);
            if (date is null || dateGroup.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var stationEntry in dateGroup.Value.EnumerateObject())
            {
                if (stationEntry.Value.ValueKind != JsonValueKind.Object)
                    continue;
                if (!int.TryParse(stationEntry.Name, out var stationId))
                    continue;

                var m = stationEntry.Value;
                daily.Add(new DailyWeather
                {
                    StationId = stationId,
                    Date = date.Value,
                    TempMax = IpmaJson.ReadMetric(m, "temp_max"),
                    TempMin = IpmaJson.ReadMetric(m, "temp_min"),
                    TempMean = IpmaJson.ReadMetric(m, "temp_med"),
                    PrecipitationMm = IpmaJson.ReadMetric(m, "prec_quant"),
                });
            }
        }

        return daily;
    }
}
