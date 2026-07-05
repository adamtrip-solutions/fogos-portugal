using Fogos.Domain.Geo;
using Fogos.Domain.Time;
using Fogos.Domain.Weather;
using MongoDB.Bson;
using static Fogos.Importer.Mapping.LegacyBson;

namespace Fogos.Importer.Mapping.Mappers;

/// <summary>
/// <c>weatherStations</c> → <c>weather_stations</c>. Coordinates here are <b>[lng,lat]</b> (GeoJSON
/// order — the opposite of incidents!); <c>geoJSON.coordinates</c> is the same pair.
/// </summary>
public sealed class WeatherStationMapper : ILegacyCollectionMapper
{
    public string Name => "weatherStations";
    public string TargetDescription => "weather_stations";

    public MapResult Map(BsonDocument doc)
    {
        var stationId = ReadInt(GetAny(doc, "stationId", "id"));
        if (stationId is null)
            return MapResult.Quarantine("no stationId");

        var coords = ReadStationPoint(doc);
        if (coords is null)
            return MapResult.Quarantine("no/invalid coordinates");

        var station = new WeatherStation
        {
            Id = stationId.Value,
            Coordinates = coords.Value,
            Name = ReadString(GetAny(doc, "location", "localEstacao")) ?? "",
            Place = ReadString(Get(doc, "place")),
            UpdatedAt = ReadDate(Get(doc, "updated"), new FogosClock()) ?? default,
        };
        return MapResult.Map(new MappedEntity(TargetCollections.WeatherStations, station));
    }

    private static GeoPoint? ReadStationPoint(BsonDocument doc)
    {
        // geoJSON.coordinates first (authoritative), then the flat coordinates array — both [lng,lat].
        if (Get(doc, "geoJSON") is { BsonType: BsonType.Document } geo
            && ReadPair(Get(geo.AsBsonDocument, "coordinates")) is { } gp
            && TryGeoJson(gp) is { } fromGeo)
            return fromGeo;
        if (ReadPair(Get(doc, "coordinates")) is { } cp && TryGeoJson(cp) is { } fromCoords)
            return fromCoords;
        return null;
    }

    private static GeoPoint? TryGeoJson((double A, double B) lngLat)
    {
        try { return GeoPoint.FromGeoJson(new[] { lngLat.A, lngLat.B }); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}

/// <summary><c>weatherData</c> → <c>weather_hourly</c>. Every metric passes the -99 sentinel filter.</summary>
public sealed class WeatherHourlyMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "weatherData";
    public string TargetDescription => "weather_hourly";

    public MapResult Map(BsonDocument doc)
    {
        var stationId = ReadInt(GetAny(doc, "stationId", "id"));
        if (stationId is null)
            return MapResult.Quarantine("no stationId");

        var at = ReadDate(Get(doc, "date"), clock);
        if (at is null)
            return MapResult.Quarantine("no date");

        // idDireccVento is also -99-guarded before decoding.
        var windDirId = Sentinel(ReadDouble(Get(doc, "idDireccVento"))) is { } d ? (int)Math.Round(d) : (int?)null;

        var obs = new WeatherObservation
        {
            Id = CarryObjectId(doc),
            StationId = stationId.Value,
            At = at.Value,
            Temperature = ReadMetric(Get(doc, "temperatura")),
            Humidity = ReadMetric(Get(doc, "humidade")),
            WindSpeedKmh = ReadMetric(Get(doc, "intensidadeVentoKM")),
            WindDirection = WindDirections.Decode(windDirId),
            PrecipitationMm = ReadMetric(Get(doc, "precAcumulada")),
            Pressure = ReadMetric(Get(doc, "pressao")),
            Radiation = ReadMetric(Get(doc, "radiacao")),
        };
        return MapResult.Map(new MappedEntity(TargetCollections.WeatherHourly, obs));
    }
}

/// <summary>
/// <c>weatherDataDaily</c> → <c>weather_daily</c>. Applies -99 → null here too, fixing the legacy
/// daily-path bug where the sentinel was never stripped.
/// </summary>
public sealed class WeatherDailyMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "weatherDataDaily";
    public string TargetDescription => "weather_daily";

    public MapResult Map(BsonDocument doc)
    {
        var stationId = ReadInt(GetAny(doc, "stationId", "id"));
        if (stationId is null)
            return MapResult.Quarantine("no stationId");

        var date = ReadDate(Get(doc, "date"), clock);
        if (date is null)
            return MapResult.Quarantine("no date");

        var daily = new DailyWeather
        {
            Id = CarryObjectId(doc),
            StationId = stationId.Value,
            Date = DateOnly.FromDateTime(date.Value.UtcDateTime),
            TempMax = ReadMetric(Get(doc, "temp_max")),
            TempMin = ReadMetric(Get(doc, "temp_min")),
            TempMean = ReadMetric(GetAny(doc, "temp_med", "temp_avg", "temp_mean")),
            PrecipitationMm = ReadMetric(GetAny(doc, "prec_quant", "prec", "precipitation")),
        };
        return MapResult.Map(new MappedEntity(TargetCollections.WeatherDaily, daily));
    }
}

/// <summary><c>weatherNormals</c> → <c>weather_normals</c>. 12-element monthly arrays; wrong length → quarantine.</summary>
public sealed class WeatherNormalMapper : ILegacyCollectionMapper
{
    public string Name => "weatherNormals";
    public string TargetDescription => "weather_normals";

    public MapResult Map(BsonDocument doc)
    {
        var stationId = ReadInt(GetAny(doc, "stationId", "id"));
        if (stationId is null)
            return MapResult.Quarantine("no stationId");

        var period = ReadString(Get(doc, "period"));
        if (period is null)
            return MapResult.Quarantine("no period");

        var tmax = ReadArray(Get(doc, "tmax_mean"));
        var tmin = ReadArray(Get(doc, "tmin_mean"));
        if (tmax is not { Length: 12 } || tmin is not { Length: 12 })
            return MapResult.Quarantine($"monthly arrays not length 12 (tmax={tmax?.Length ?? 0}, tmin={tmin?.Length ?? 0})");

        var normal = new WeatherNormal
        {
            Id = CarryObjectId(doc),
            StationId = stationId.Value,
            Period = period,
            TmaxMean = tmax,
            TminMean = tmin,
        };
        return MapResult.Map(new MappedEntity(TargetCollections.WeatherNormals, normal));
    }

    private static double[]? ReadArray(BsonValue? v)
    {
        if (v is not { BsonType: BsonType.Array })
            return null;
        var arr = v.AsBsonArray;
        var result = new double[arr.Count];
        for (var i = 0; i < arr.Count; i++)
            result[i] = ReadDouble(arr[i]) ?? 0;
        return result;
    }
}

/// <summary><c>temperatureWaves</c> → <c>temperature_waves</c>. Day entries are <c>{date,value,delta}</c>.</summary>
public sealed class TemperatureWaveMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "temperatureWaves";
    public string TargetDescription => "temperature_waves";

    public MapResult Map(BsonDocument doc)
    {
        var stationId = ReadInt(GetAny(doc, "stationId", "id"));
        if (stationId is null)
            return MapResult.Quarantine("no stationId");

        var type = ReadString(Get(doc, "type"))?.ToLowerInvariant() switch
        {
            "heat" => WaveType.Heat,
            "cold" => WaveType.Cold,
            _ => (WaveType?)null,
        };
        if (type is null)
            return MapResult.Quarantine("unknown wave type (expected heat/cold)");

        var start = ReadDate(Get(doc, "start_date"), clock);
        if (start is null)
            return MapResult.Quarantine("no start_date");
        var end = ReadDate(Get(doc, "end_date"), clock) ?? start;

        var wave = new TemperatureWave
        {
            Id = CarryObjectId(doc),
            StationId = stationId.Value,
            Type = type.Value,
            StartDate = DateOnly.FromDateTime(start.Value.UtcDateTime),
            EndDate = DateOnly.FromDateTime(end.Value.UtcDateTime),
            Ongoing = ReadBool(Get(doc, "ongoing")),
            Days = ReadDays(Get(doc, "days")),
            UpdatedAt = ReadDate(Get(doc, "updated"), clock) ?? default,
        };
        return MapResult.Map(new MappedEntity(TargetCollections.TemperatureWaves, wave));
    }

    private static List<WaveDay> ReadDays(BsonValue? v)
    {
        var days = new List<WaveDay>();
        if (v is not { BsonType: BsonType.Array })
            return days;
        foreach (var el in v.AsBsonArray)
        {
            if (el is not { BsonType: BsonType.Document })
                continue;
            var d = el.AsBsonDocument;
            var dateStr = ReadString(Get(d, "date"));
            if (dateStr is null || !DateOnly.TryParse(dateStr, out var date))
                continue;
            var observed = ReadDouble(GetAny(d, "value", "observed")) ?? 0;
            var deviation = ReadDouble(GetAny(d, "delta", "deviation")) ?? 0;
            var normal = ReadDouble(Get(d, "normal")) ?? observed - deviation;
            days.Add(new WaveDay(date, observed, normal, deviation));
        }
        return days;
    }
}

/// <summary>
/// <c>weather_warnings</c> → <c>weather_warnings</c>. Stored fields are <c>district</c> (= idAreaAviso)
/// and <c>type</c> (= awarenessTypeName); <c>control</c> is the dedup hash and is required.
/// </summary>
public sealed class WeatherWarningMapper(IClock clock) : ILegacyCollectionMapper
{
    public string Name => "weather_warnings";
    public string TargetDescription => "weather_warnings";

    public MapResult Map(BsonDocument doc)
    {
        var control = ReadString(GetAny(doc, "control", "hash"));
        if (control is null)
            return MapResult.Quarantine("no control hash");

        var warning = new WeatherWarning
        {
            Id = CarryObjectId(doc),
            AreaCode = ReadString(GetAny(doc, "idAreaAviso", "district", "area")) ?? "",
            AwarenessType = ReadString(GetAny(doc, "awarenessTypeName", "type")) ?? "",
            Level = ReadString(Get(doc, "level")) ?? "",
            StartsAt = ReadDate(GetAny(doc, "startTime", "start"), clock) ?? default,
            EndsAt = ReadDate(GetAny(doc, "endTime", "end"), clock) ?? default,
            Text = ReadString(Get(doc, "text")),
            Control = control,
            CreatedAt = ReadDate(Get(doc, "created"), clock) ?? clock.UtcNow,
        };
        return MapResult.Map(new MappedEntity(TargetCollections.WeatherWarnings, warning));
    }
}
