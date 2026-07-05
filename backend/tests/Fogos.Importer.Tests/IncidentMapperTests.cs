using Fogos.Domain.Incidents;
using Fogos.Domain.Social;
using Fogos.Importer.Mapping;
using Fogos.Importer.Mapping.Mappers;
using MongoDB.Bson;

namespace Fogos.Importer.Tests;

public class IncidentMapperTests
{
    private static readonly IncidentMapper Mapper = new(Mapping.Clock);

    [Fact]
    public void Happy_path_maps_dual_id_sec_dates_coords_dico_icnf_and_thread()
    {
        var doc = Fixtures.Load("data", "incident_happy");
        var result = Mapping.MapOne(Mapper, doc);

        var incident = Mapping.MappedSingle<Incident>(result);

        // Business id is the string `id`, not the ObjectId `_id`.
        Assert.Equal("2022080812345", incident.Id);

        // {sec:N} → UTC instant.
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1660000000), incident.OccurredAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1660000100), incident.CreatedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1660000200), incident.UpdatedAt);

        // [lat,lng] array honoured (41.1 is the latitude).
        Assert.NotNull(incident.Coordinates);
        Assert.Equal(41.1, incident.Coordinates!.Value.Latitude, 5);
        Assert.Equal(-8.6, incident.Coordinates!.Value.Longitude, 5);

        // "111" → "0111".
        Assert.Equal("0111", incident.Dico);

        // naturezaCode 3101 classifies as Fire.
        Assert.Equal(IncidentKind.Fire, incident.Kind);
        Assert.Equal(IncidentStatusCatalog.EmCurso, incident.Status.Code);

        // Resources incl. aquatic + heli/plane breakdown.
        Assert.Equal(42, incident.Resources.Man);
        Assert.Equal(1, incident.Resources.Aquatic);
        Assert.Equal(40, incident.Resources.ManGround);
        Assert.Equal(5, incident.Resources.ManAerial);
        Assert.Equal(3, incident.Resources.Entities);
        Assert.Equal(2, incident.Resources.HeliFight);
        Assert.Equal(1, incident.Resources.PlaneFight);

        // ICNF sub-doc (legacy field names) + top-level species/family.
        Assert.NotNull(incident.Icnf);
        Assert.Equal(20.0, incident.Icnf!.BurnArea!.Total);
        Assert.Equal("Intencional", incident.Icnf.CauseType);
        Assert.Equal("Uso do fogo", incident.Icnf.CauseFamily);
        Assert.Equal("Popular", incident.Icnf.AlertSource);
        Assert.Equal("Pinheiro", incident.Icnf.SpeciesName);
        Assert.Equal("Resinosas", incident.Icnf.FamilyName);

        Assert.Equal("<kml>perimeter</kml>", incident.Kml);
        Assert.Equal(560, incident.NearestWeatherStationId);
        Assert.True(incident.Important);

        // SocialThread extracted from lastTweetId / facebookPostId / flags.
        var thread = Mapping.MappedSingle<SocialThread>(result);
        Assert.Equal("2022080812345", thread.IncidentId);
        Assert.Equal("1556677889900112233", thread.LastTweetId);
        Assert.Equal("998877", thread.FacebookPostId);
        Assert.True(thread.SentImportantPost);
        Assert.False(thread.SentBigIncidentPost);
    }

    [Fact]
    public void Means_fields_default_to_zero_when_absent()
    {
        var doc = new BsonDocument
        {
            ["id"] = "x3",
            ["dateTime"] = new BsonDocument("sec", 1660000000),
            ["location"] = "X",
            ["statusCode"] = 5,
            ["naturezaCode"] = "3101",
        };
        var incident = Mapping.MappedSingle<Incident>(Mapping.MapOne(Mapper, doc));
        Assert.Equal(0, incident.Resources.ManGround);
        Assert.Equal(0, incident.Resources.ManAerial);
        Assert.Equal(0, incident.Resources.Entities);
    }

    [Fact]
    public void Uses_string_underscore_id_as_business_key_when_no_top_level_id()
    {
        var doc = BsonDocument.Parse("""
        { "_id": "2021070100042", "dateTime": {"sec": 1625097600}, "location": "X",
          "statusCode": 5, "naturezaCode": "3101" }
        """);
        var incident = Mapping.MappedSingle<Incident>(Mapping.MapOne(Mapper, doc));
        Assert.Equal("2021070100042", incident.Id);
    }

    [Theory]
    [InlineData("  DESPACHO DE 1º ALERTA")]
    [InlineData("Despacho de 1.º Alerta")]
    [InlineData("Despacho de 1º Alerta")]
    public void Dirty_status_label_without_code_normalizes_to_code_4(string label)
    {
        var doc = new BsonDocument
        {
            ["id"] = "x1",
            ["dateTime"] = new BsonDocument("sec", 1660000000),
            ["location"] = "X",
            ["status"] = label,
            ["naturezaCode"] = "3101",
        };
        var incident = Mapping.MappedSingle<Incident>(Mapping.MapOne(Mapper, doc));
        Assert.Equal(IncidentStatusCatalog.DespachoPrimeiroAlerta, incident.Status.Code);
        Assert.Equal("Despacho de 1º Alerta", incident.Status.Label);
    }

    [Fact]
    public void No_business_key_quarantines()
    {
        var doc = Fixtures.Load("data", "incident_no_business_key");
        var q = Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(Mapper, doc));
        Assert.Contains("business id", q.Reason);
    }

    [Fact]
    public void Unmappable_status_quarantines()
    {
        var doc = new BsonDocument
        {
            ["id"] = "x2",
            ["dateTime"] = new BsonDocument("sec", 1660000000),
            ["location"] = "X",
            ["status"] = "Estado Inventado",
            ["naturezaCode"] = "3101",
        };
        var q = Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(Mapper, doc));
        Assert.Contains("status", q.Reason);
    }

    [Fact]
    public void Swapped_coordinates_are_recovered()
    {
        // lat=-8.6, lng=41.1 is geographically swapped; recovery yields lat 41.1, lng -8.6.
        var doc = Fixtures.Load("data", "incident_swapped_coords");
        var incident = Mapping.MappedSingle<Incident>(Mapping.MapOne(Mapper, doc));
        Assert.NotNull(incident.Coordinates);
        Assert.Equal(41.1, incident.Coordinates!.Value.Latitude, 5);
        Assert.Equal(-8.6, incident.Coordinates!.Value.Longitude, 5);
    }

    [Fact]
    public void Out_of_range_coordinates_become_null_not_quarantine()
    {
        var doc = new BsonDocument
        {
            ["id"] = "x3",
            ["dateTime"] = new BsonDocument("sec", 1660000000),
            ["location"] = "X",
            ["statusCode"] = 5,
            ["naturezaCode"] = "3101",
            ["lat"] = 999.0,
            ["lng"] = 999.0,
        };
        var incident = Mapping.MappedSingle<Incident>(Mapping.MapOne(Mapper, doc));
        Assert.Null(incident.Coordinates);
    }

    [Theory]
    [InlineData("isUrbanFire", IncidentKind.UrbanFire)]
    [InlineData("isTransporteFire", IncidentKind.TransportFire)]
    [InlineData("isOtherFire", IncidentKind.OtherFire)]
    [InlineData("isFMA", IncidentKind.Fma)]
    public void Kind_derives_from_isX_booleans_when_no_natureza_code(string flag, IncidentKind expected)
    {
        var doc = new BsonDocument
        {
            ["id"] = "x4",
            ["dateTime"] = new BsonDocument("sec", 1660000000),
            ["location"] = "X",
            ["statusCode"] = 5,
            [flag] = true,
        };
        var incident = Mapping.MappedSingle<Incident>(Mapping.MapOne(Mapper, doc));
        Assert.Equal(expected, incident.Kind);
    }

    [Fact]
    public void Active_defaults_from_status_code_when_absent()
    {
        var active = new BsonDocument
        {
            ["id"] = "a1", ["dateTime"] = new BsonDocument("sec", 1660000000), ["location"] = "X",
            ["statusCode"] = 5, ["naturezaCode"] = "3101",
        };
        var inactive = new BsonDocument
        {
            ["id"] = "a2", ["dateTime"] = new BsonDocument("sec", 1660000000), ["location"] = "X",
            ["statusCode"] = 8, ["naturezaCode"] = "3101",
        };
        Assert.True(Mapping.MappedSingle<Incident>(Mapping.MapOne(Mapper, active)).Active);
        Assert.False(Mapping.MappedSingle<Incident>(Mapping.MapOne(Mapper, inactive)).Active);
    }

    [Fact]
    public void No_thread_fields_yields_incident_only()
    {
        var doc = new BsonDocument
        {
            ["id"] = "n1", ["dateTime"] = new BsonDocument("sec", 1660000000), ["location"] = "X",
            ["statusCode"] = 5, ["naturezaCode"] = "3101",
        };
        var result = Mapping.MapOne(Mapper, doc);
        Assert.Null(Mapping.MappedOrNull<SocialThread>(result));
        Assert.NotNull(Mapping.MappedOrNull<Incident>(result));
    }
}
