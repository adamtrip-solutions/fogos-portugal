using Fogos.Domain.Aircraft;
using Fogos.Domain.Locations;
using Fogos.Domain.Risk;
using Fogos.Domain.Stats;
using Fogos.Importer.Mapping;
using Fogos.Importer.Mapping.Mappers;
using MongoDB.Bson;

namespace Fogos.Importer.Tests;

public class FlightAndAircraftTests
{
    [Fact]
    public void Flight_position_lat_lon_to_geopoint()
    {
        var mapper = new FlightPositionMapper(Mapping.Clock);
        var doc = BsonDocument.Parse("""
        { "icao": "abc123", "registration": "CS-XYZ", "lat": 41.1, "lon": -8.6, "altitude": 1500,
          "sampled_at": {"$date":"2023-07-01T12:00:00Z"}, "source": "fr24", "fr24_id": "xyz" }
        """);
        var pos = Mapping.MappedSingle<FlightPosition>(Mapping.MapOne(mapper, doc));
        Assert.Equal("abc123", pos.Icao);
        Assert.Equal(41.1, pos.Position.Latitude, 5);
        Assert.Equal(-8.6, pos.Position.Longitude, 5);
        Assert.Equal("fr24", pos.Source);
        Assert.Equal("xyz", pos.Fr24Id);
    }

    [Fact]
    public void Flight_position_bad_coordinates_quarantines()
    {
        var mapper = new FlightPositionMapper(Mapping.Clock);
        var doc = BsonDocument.Parse("""{ "icao": "a", "registration": "b", "sampled_at": {"$date":"2023-07-01T12:00:00Z"}, "source": "x" }""");
        Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(mapper, doc));
    }

    [Fact]
    public void Tracked_aircraft_maps_icao_as_id_and_defaults_active_true()
    {
        var mapper = new TrackedAircraftMapper();
        var doc = BsonDocument.Parse("""{ "icao": "abc123", "registration": "CS-XYZ", "kind": "helicopter", "notify": true }""");
        var a = Mapping.MappedSingle<TrackedAircraft>(Mapping.MapOne(mapper, doc));
        Assert.Equal("abc123", a.Icao);
        Assert.True(a.Active);
        Assert.True(a.Notify);
    }
}

public class LocationMapperTests
{
    private static readonly LocationMapper Mapper = new();

    [Fact]
    public void Concelho_gets_zero_padded_dico()
    {
        var doc = BsonDocument.Parse("""{ "level": 2, "code": "111", "name": "Sabugal" }""");
        var loc = Mapping.MappedSingle<Location>(Mapping.MapOne(Mapper, doc));
        Assert.Equal(LocationLevel.Concelho, loc.Level);
        Assert.Equal("0111", loc.Dico);
    }

    [Fact]
    public void Distrito_has_no_dico()
    {
        var doc = BsonDocument.Parse("""{ "level": 1, "code": "11", "name": "Guarda" }""");
        var loc = Mapping.MappedSingle<Location>(Mapping.MapOne(Mapper, doc));
        Assert.Null(loc.Dico);
    }

    [Fact]
    public void Unknown_level_quarantines()
    {
        Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(Mapper, BsonDocument.Parse("""{ "level": 3, "code": "x", "name": "y" }""")));
    }
}

public class RiskMapperTests
{
    [Fact]
    public void Rcm_daily_maps_all_horizons_and_pads_dico()
    {
        var mapper = new RcmDailyMapper(Mapping.Clock);
        var doc = BsonDocument.Parse("""
        { "concelho": "Sabugal", "dico": "111", "date": {"$date":"2023-07-01T00:00:00Z"},
          "hoje": 4, "amanha": 5, "depois": 3, "depois2": 2, "depois3": 1 }
        """);
        var risk = Mapping.MappedSingle<ConcelhoRisk>(Mapping.MapOne(mapper, doc));
        Assert.Equal("0111", risk.Dico);
        Assert.Equal(4, risk.Today);
        Assert.Equal(5, risk.Tomorrow);
        Assert.Equal(1, risk.After3);
    }

    [Fact]
    public void Rcm_geojson_maps_when_and_keeps_remaining_payload()
    {
        var mapper = new RcmGeoJsonMapper(Mapping.Clock);
        var doc = BsonDocument.Parse("""
        {
          "_id": { "$oid": "5f1d7f0a9b2c3d4e5f6a7b90" },
          "when": "amanha",
          "dataPrev": {"$date":"2023-07-02T00:00:00Z"},
          "dataRun":  {"$date":"2023-07-01T09:00:00Z"},
          "local": { "0111": { "data": { "rcm": 4 } } }
        }
        """);
        var geo = Mapping.MappedSingle<RiskGeoJson>(Mapping.MapOne(mapper, doc));
        Assert.Equal(RiskDay.Tomorrow, geo.When);
        Assert.Equal(new DateOnly(2023, 7, 2), geo.ForecastDate);
        Assert.NotNull(geo.RunAt);
        Assert.Contains("local", geo.GeoJson);
        Assert.DoesNotContain("dataPrev", geo.GeoJson); // mapped scalars stripped
    }

    [Fact]
    public void Rcm_geojson_unknown_when_quarantines()
    {
        var mapper = new RcmGeoJsonMapper(Mapping.Clock);
        Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(mapper, BsonDocument.Parse("""{ "when": "ontem", "dataPrev": {"$date":"2023-07-02T00:00:00Z"} }""")));
    }
}

public class HistoryTotalMapperTests
{
    [Fact]
    public void Maps_totals()
    {
        var mapper = new HistoryTotalMapper(Mapping.Clock);
        var doc = BsonDocument.Parse("""{ "created": {"$date":"2023-07-01T12:00:00Z"}, "man": 500, "terrain": 120, "aerial": 8, "total": 628 }""");
        var t = Mapping.MappedSingle<HistoryTotal>(Mapping.MapOne(mapper, doc));
        Assert.Equal(500, t.Man);
        Assert.Equal(628, t.Total);
    }
}

public class RegistryTests
{
    [Fact]
    public void Default_collections_exclude_dead_ones()
    {
        var registry = new MapperRegistry(Mapping.Clock);
        Assert.DoesNotContain("pplanes", registry.DefaultCollections);
        Assert.DoesNotContain("warningMadeira", registry.DefaultCollections);
        Assert.DoesNotContain("users", registry.DefaultCollections);
        Assert.Contains("data", registry.DefaultCollections);
        Assert.Contains("weatherData", registry.DefaultCollections);
    }

    [Fact]
    public void Dead_collections_are_registered_as_skip_only()
    {
        var registry = new MapperRegistry(Mapping.Clock);
        Assert.True(registry.TryGet("pplanes", out var mapper));
        var result = mapper.Map(BsonDocument.Parse("""{ "x": 1 }"""));
        var skip = Assert.IsType<MapResult.Skipped>(result);
        Assert.NotEmpty(skip.Reason);
    }

    [Fact]
    public void Every_default_collection_has_a_mapper()
    {
        var registry = new MapperRegistry(Mapping.Clock);
        foreach (var c in registry.DefaultCollections)
            Assert.True(registry.TryGet(c, out _));
    }
}
