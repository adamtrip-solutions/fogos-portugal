using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Warnings;
using Fogos.Importer.Mapping;
using Fogos.Importer.Mapping.Mappers;
using MongoDB.Bson;

namespace Fogos.Importer.Tests;

public class StatusHistoryMapperTests
{
    private static readonly StatusHistoryMapper Mapper = new(Mapping.Clock);

    [Theory]
    // Winter: Europe/Lisbon is UTC+0, so 10:30 local = 10:30 UTC.
    [InlineData("15-01-2023 10:30", "2023-01-15T10:30:00Z")]
    // Summer: Europe/Lisbon is UTC+1 (WEST), so 10:30 local = 09:30 UTC.
    [InlineData("15-07-2023 10:30", "2023-07-15T09:30:00Z")]
    public void Legacy_d_m_Y_string_created_is_interpreted_as_Lisbon(string legacy, string expectedUtc)
    {
        var doc = new BsonDocument
        {
            ["id"] = "inc-1",
            ["created"] = legacy,
            ["statusCode"] = 5,
        };
        var change = Mapping.MappedSingle<IncidentStatusChange>(Mapping.MapOne(Mapper, doc));
        Assert.Equal(DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
            change.At);
        Assert.Equal("inc-1", change.IncidentId);
        Assert.Equal(5, change.Code);
    }

    [Fact]
    public void Status_label_without_code_normalizes()
    {
        var doc = new BsonDocument { ["id"] = "inc-2", ["created"] = "01-08-2023 00:00", ["status"] = " Encerrada" };
        var change = Mapping.MappedSingle<IncidentStatusChange>(Mapping.MapOne(Mapper, doc));
        Assert.Equal(IncidentStatusCatalog.Encerrada, change.Code);
    }

    [Fact]
    public void No_fk_quarantines()
    {
        var doc = new BsonDocument { ["created"] = "01-08-2023 00:00", ["statusCode"] = 5 };
        Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(Mapper, doc));
    }

    [Fact]
    public void Unknown_status_quarantines()
    {
        var doc = new BsonDocument { ["id"] = "inc-3", ["created"] = "01-08-2023 00:00", ["status"] = "???" };
        Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(Mapper, doc));
    }
}

public class HistoryMapperTests
{
    private static readonly IncidentHistoryMapper Mapper = new(Mapping.Clock);

    [Fact]
    public void Maps_fk_and_resources()
    {
        var doc = BsonDocument.Parse("""
        { "incidentId": "inc-9", "created": {"$date":"2023-07-01T12:00:00Z"}, "man": 20, "terrain": 5, "aerial": 2, "location": "sítio" }
        """);
        var snap = Mapping.MappedSingle<IncidentHistorySnapshot>(Mapping.MapOne(Mapper, doc));
        Assert.Equal("inc-9", snap.IncidentId);
        Assert.Equal(20, snap.Man);
        Assert.Equal("sítio", snap.Location);
    }

    [Fact]
    public void Falls_back_to_id_for_fk()
    {
        var doc = BsonDocument.Parse("""{ "id": "inc-10", "created": {"$date":"2023-07-01T12:00:00Z"}, "man": 1 }""");
        Assert.Equal("inc-10", Mapping.MappedSingle<IncidentHistorySnapshot>(Mapping.MapOne(Mapper, doc)).IncidentId);
    }

    [Fact]
    public void No_fk_quarantines()
    {
        var doc = BsonDocument.Parse("""{ "created": {"$date":"2023-07-01T12:00:00Z"}, "man": 1 }""");
        Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(Mapper, doc));
    }
}

public class IncidentPhotoMapperTests
{
    private static readonly IncidentPhotoMapper Mapper = new(Mapping.Clock);

    [Fact]
    public void Maps_created_at_updated_at_and_gps_object()
    {
        var doc = BsonDocument.Parse("""
        {
          "fire_id": "inc-7", "status": "approved", "public": true, "storage_key": "photos/abc.jpg",
          "size_bytes": 123456, "width": 1920, "height": 1080, "mime": "image/jpeg",
          "gps": { "lat": 41.1, "lng": -8.6 },
          "taken_at": {"$date":"2023-07-01T12:00:00Z"},
          "created_at": {"$date":"2023-07-01T12:05:00Z"},
          "updated_at": {"$date":"2023-07-01T12:06:00Z"}
        }
        """);
        var photo = Mapping.MappedSingle<IncidentPhoto>(Mapping.MapOne(Mapper, doc));
        Assert.Equal("inc-7", photo.IncidentId);
        Assert.Equal(ModerationStatus.Approved, photo.Status);
        Assert.True(photo.Public);
        Assert.Equal("photos/abc.jpg", photo.StorageKey);
        Assert.NotNull(photo.Gps);
        Assert.Equal(41.1, photo.Gps!.Value.Latitude, 5);
        Assert.Equal(DateTimeOffset.Parse("2023-07-01T12:05:00Z"), photo.CreatedAt);
    }

    [Fact]
    public void Missing_storage_key_quarantines()
    {
        var doc = BsonDocument.Parse("""{ "fire_id": "inc-7", "status": "approved" }""");
        var q = Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(Mapper, doc));
        Assert.Contains("storage_key", q.Reason);
    }

    [Fact]
    public void Missing_fire_id_quarantines()
    {
        var doc = BsonDocument.Parse("""{ "storage_key": "photos/abc.jpg" }""");
        Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(Mapper, doc));
    }
}

public class HotspotMapperTests
{
    private static readonly HotspotMapper Mapper = new(Mapping.Clock);

    [Fact]
    public void Maps_viirs_samples_with_acquired_at()
    {
        var doc = BsonDocument.Parse("""
        {
          "incident_id": "inc-5",
          "viirs": [ { "latitude": 41.1, "longitude": -8.6, "brightness": 330.5, "confidence": "n",
                       "acq_date": "2023-07-01", "acq_time": "1330" } ],
          "modis": [],
          "fetched_at": {"$date":"2023-07-01T14:00:00Z"}
        }
        """);
        var hs = Mapping.MappedSingle<Hotspots>(Mapping.MapOne(Mapper, doc));
        Assert.Equal("inc-5", hs.IncidentId);
        var sample = Assert.Single(hs.Viirs);
        Assert.Equal(41.1, sample.Position.Latitude, 5);
        Assert.Equal(330.5, sample.Brightness);
        Assert.Equal(new DateTimeOffset(2023, 7, 1, 13, 30, 0, TimeSpan.Zero), sample.AcquiredAt);
    }

    [Fact]
    public void Missing_incident_id_quarantines()
    {
        Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(Mapper, BsonDocument.Parse("""{ "viirs": [] }""")));
    }
}

public class WarningMapperTests
{
    [Fact]
    public void All_three_legacy_collections_unify_to_warnings_with_distinct_kinds()
    {
        var manual = new WarningMapper("warning", WarningKind.Manual, Mapping.Clock);
        var agif = new WarningMapper("warning_agif", WarningKind.Agif, Mapping.Clock);
        var site = new WarningMapper("warningSite", WarningKind.Site, Mapping.Clock);

        var doc = BsonDocument.Parse("""{ "text": "Cuidado com o fogo", "created": {"$date":"2023-07-01T00:00:00Z"} }""");

        var m = Mapping.MappedSingle<Warning>(Mapping.MapOne(manual, doc));
        var a = Mapping.MappedSingle<Warning>(Mapping.MapOne(agif, doc));
        var s = Mapping.MappedSingle<Warning>(Mapping.MapOne(site, doc));

        Assert.All(new[] { m, a, s }, w =>
        {
            Assert.Equal("Cuidado com o fogo", w.Message);
            Assert.Equal(TargetCollections.Warnings, TargetCollections.Warnings);
        });
        Assert.Equal(WarningKind.Manual, m.Kind);
        Assert.Equal(WarningKind.Agif, a.Kind);
        Assert.Equal(WarningKind.Site, s.Kind);
    }

    [Fact]
    public void All_three_map_to_the_single_warnings_collection()
    {
        foreach (var (name, kind) in new[] { ("warning", WarningKind.Manual), ("warning_agif", WarningKind.Agif), ("warningSite", WarningKind.Site) })
        {
            var mapper = new WarningMapper(name, kind, Mapping.Clock);
            var doc = BsonDocument.Parse("""{ "text": "x" }""");
            var mapped = Assert.IsType<MapResult.Mapped>(Mapping.MapOne(mapper, doc));
            Assert.Equal(TargetCollections.Warnings, Assert.Single(mapped.Entities).Collection);
        }
    }

    [Fact]
    public void Missing_text_quarantines()
    {
        var mapper = new WarningMapper("warning", WarningKind.Manual, Mapping.Clock);
        Assert.IsType<MapResult.Quarantined>(Mapping.MapOne(mapper, BsonDocument.Parse("""{ "id": "w1" }""")));
    }
}
