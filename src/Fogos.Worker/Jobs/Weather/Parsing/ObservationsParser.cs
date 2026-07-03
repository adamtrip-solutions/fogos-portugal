using System.Text.Json;
using Fogos.Domain.Weather;

namespace Fogos.Worker.Jobs.Weather.Parsing;

/// <summary>
/// Parses IPMA hourly <c>observations.json</c>: a map of <c>timestamp → (stationId → metrics|null)</c>.
/// Port of <c>UpdateWeatherData.php</c>: field mapping temperatura/humidade/intensidadeVentoKM/
/// idDireccVento (decoded via <see cref="WindDirections"/>)/precAcumulada/pressao/radiacao, with the
/// <c>-99 → null</c> sentinel applied to every metric (including idDireccVento before decoding).
/// </summary>
public static class ObservationsParser
{
    public static IReadOnlyList<WeatherObservation> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var observations = new List<WeatherObservation>();

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return observations;

        foreach (var timeGroup in doc.RootElement.EnumerateObject())
        {
            var at = IpmaJson.ParseUtc(timeGroup.Name);
            if (at is null || timeGroup.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var stationEntry in timeGroup.Value.EnumerateObject())
            {
                // Legacy skips null station payloads (`if($d)`).
                if (stationEntry.Value.ValueKind != JsonValueKind.Object)
                    continue;
                if (!int.TryParse(stationEntry.Name, out var stationId))
                    continue;

                var m = stationEntry.Value;
                var windDirId = IpmaJson.Sentinel(IpmaJson.ReadNumber(IpmaJson.Prop(m, "idDireccVento")));

                observations.Add(new WeatherObservation
                {
                    StationId = stationId,
                    At = at.Value,
                    Temperature = IpmaJson.ReadMetric(m, "temperatura"),
                    Humidity = IpmaJson.ReadMetric(m, "humidade"),
                    WindSpeedKmh = IpmaJson.ReadMetric(m, "intensidadeVentoKM"),
                    WindDirection = WindDirections.Decode(windDirId is null ? null : (int)Math.Round(windDirId.Value)),
                    PrecipitationMm = IpmaJson.ReadMetric(m, "precAcumulada"),
                    Pressure = IpmaJson.ReadMetric(m, "pressao"),
                    Radiation = IpmaJson.ReadMetric(m, "radiacao"),
                });
            }
        }

        return observations;
    }
}
