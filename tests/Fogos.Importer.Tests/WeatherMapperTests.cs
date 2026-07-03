using Fogos.Domain.Weather;
using Fogos.Importer.Mapping;
using Fogos.Importer.Mapping.Mappers;
using MongoDB.Bson;

namespace Fogos.Importer.Tests;

public class WeatherMapperTests
{
    [Fact]
    public void Hourly_neg99_sentinels_become_null_on_every_metric()
    {
        var mapper = new WeatherHourlyMapper(Mapping.Clock);
        var doc = BsonDocument.Parse("""
        {
          "stationId": "560",
          "date": { "$date": "2023-07-01T12:00:00Z" },
          "temperatura": -99,
          "humidade": -99.0,
          "intensidadeVentoKM": "−99",
          "idDireccVento": -99,
          "precAcumulada": -99,
          "pressao": -99,
          "radiacao": -99
        }
        """);
        var obs = Mapping.MappedSingle<WeatherObservation>(Mapping.MapOne(mapper, doc));
        Assert.Equal(560, obs.StationId);
        Assert.Null(obs.Temperature);
        Assert.Null(obs.Humidity);
        Assert.Null(obs.WindSpeedKmh);
        Assert.Null(obs.WindDirection);
        Assert.Null(obs.PrecipitationMm);
        Assert.Null(obs.Pressure);
        Assert.Null(obs.Radiation);
    }

    [Fact]
    public void Hourly_real_values_and_wind_direction_decode()
    {
        var mapper = new WeatherHourlyMapper(Mapping.Clock);
        var doc = BsonDocument.Parse("""
        {
          "stationId": "560",
          "date": { "$date": "2023-07-01T12:00:00Z" },
          "temperatura": 31.4, "humidade": 22, "idDireccVento": 1
        }
        """);
        var obs = Mapping.MappedSingle<WeatherObservation>(Mapping.MapOne(mapper, doc));
        Assert.Equal(31.4, obs.Temperature);
        Assert.Equal("N", obs.WindDirection);
    }

    [Fact]
    public void Daily_neg99_sentinels_become_null()
    {
        var mapper = new WeatherDailyMapper(Mapping.Clock);
        var doc = BsonDocument.Parse("""
        {
          "stationId": 560,
          "date": { "$date": "2023-07-01T00:00:00Z" },
          "temp_max": 38.2, "temp_min": -99, "temp_med": -99.0, "prec_quant": -99
        }
        """);
        var daily = Mapping.MappedSingle<DailyWeather>(Mapping.MapOne(mapper, doc));
        Assert.Equal(new DateOnly(2023, 7, 1), daily.Date);
        Assert.Equal(38.2, daily.TempMax);
        Assert.Null(daily.TempMin);
        Assert.Null(daily.TempMean);
        Assert.Null(daily.PrecipitationMm);
    }

    [Fact]
    public void Station_coordinates_are_lng_lat_order()
    {
        var mapper = new WeatherStationMapper();
        // GeoJSON order [lng, lat]: Lisbon is (lng -9.14, lat 38.72).
        var doc = BsonDocument.Parse("""
        { "stationId": 579, "coordinates": [-9.14, 38.72], "location": "Lisboa (Geofísico)", "place": "Portugal" }
        """);
        var station = Mapping.MappedSingle<WeatherStation>(Mapping.MapOne(mapper, doc));
        Assert.Equal(579, station.Id);
        Assert.Equal(38.72, station.Coordinates.Latitude, 5);
        Assert.Equal(-9.14, station.Coordinates.Longitude, 5);
        Assert.Equal("Lisboa (Geofísico)", station.Name);
        Assert.Equal("Portugal", station.Place);
    }

    [Fact]
    public void Station_prefers_geojson_coordinates_subdoc()
    {
        var mapper = new WeatherStationMapper();
        var doc = BsonDocument.Parse("""
        { "stationId": 579, "geoJSON": { "type": "Point", "coordinates": [-9.14, 38.72] }, "location": "X" }
        """);
        var station = Mapping.MappedSingle<WeatherStation>(Mapping.MapOne(mapper, doc));
        Assert.Equal(38.72, station.Coordinates.Latitude, 5);
    }

    [Fact]
    public void Normals_wrong_array_length_quarantines()
    {
        var mapper = new WeatherNormalMapper();
        var doc = BsonDocument.Parse("""
        { "stationId": 560, "period": "1991-2020", "tmax_mean": [1,2,3], "tmin_mean": [1,2,3] }
        """);
        var q = Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(mapper, doc));
        Assert.Contains("length 12", q.Reason);
    }

    [Fact]
    public void Normals_twelve_element_arrays_map()
    {
        var mapper = new WeatherNormalMapper();
        var doc = BsonDocument.Parse("""
        { "stationId": 560, "period": "1991-2020",
          "tmax_mean": [10,11,14,16,20,25,29,29,25,20,14,11],
          "tmin_mean": [3,4,6,7,10,14,16,16,14,10,6,4] }
        """);
        var normal = Mapping.MappedSingle<WeatherNormal>(Mapping.MapOne(mapper, doc));
        Assert.Equal(12, normal.TmaxMean.Length);
        Assert.Equal("1991-2020", normal.Period);
    }

    [Fact]
    public void Temperature_wave_days_map_with_deviation()
    {
        var mapper = new TemperatureWaveMapper(Mapping.Clock);
        var doc = BsonDocument.Parse("""
        {
          "stationId": 560, "type": "heat", "ongoing": true,
          "start_date": { "$date": "2023-07-01T00:00:00Z" },
          "end_date":   { "$date": "2023-07-06T00:00:00Z" },
          "days": [ { "date": "2023-07-01", "value": 38.0, "delta": 8.0 } ]
        }
        """);
        var wave = Mapping.MappedSingle<TemperatureWave>(Mapping.MapOne(mapper, doc));
        Assert.Equal(WaveType.Heat, wave.Type);
        Assert.True(wave.Ongoing);
        Assert.Equal(new DateOnly(2023, 7, 1), wave.StartDate);
        var day = Assert.Single(wave.Days);
        Assert.Equal(38.0, day.Observed);
        Assert.Equal(8.0, day.Deviation);
        Assert.Equal(30.0, day.Normal, 5); // value - delta
    }

    [Fact]
    public void Weather_warning_requires_control()
    {
        var mapper = new WeatherWarningMapper(Mapping.Clock);
        var doc = BsonDocument.Parse("""{ "type": "Tempo Quente", "district": "AVR", "level": "orange" }""");
        Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(mapper, doc));
    }

    [Fact]
    public void Weather_warning_maps_area_and_type_from_stored_field_names()
    {
        var mapper = new WeatherWarningMapper(Mapping.Clock);
        var doc = BsonDocument.Parse("""
        { "type": "Tempo Quente", "district": "AVR", "level": "orange", "text": "calor", "control": "abc123",
          "startTime": {"$date":"2023-07-01T00:00:00Z"}, "endTime": {"$date":"2023-07-02T00:00:00Z"} }
        """);
        var w = Mapping.MappedSingle<WeatherWarning>(Mapping.MapOne(mapper, doc));
        Assert.Equal("AVR", w.AreaCode);
        Assert.Equal("Tempo Quente", w.AwarenessType);
        Assert.Equal("abc123", w.Control);
    }
}
