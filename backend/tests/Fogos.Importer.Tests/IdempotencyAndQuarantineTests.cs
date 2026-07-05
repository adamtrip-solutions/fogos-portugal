using Fogos.Importer;
using Fogos.Importer.Mapping;
using Fogos.Importer.Mapping.Mappers;
using MongoDB.Bson;

namespace Fogos.Importer.Tests;

public class IdempotencyTests
{
    /// <summary>
    /// Mapping the same doc twice must yield byte-identical serialized entities — this is what
    /// makes the ReplaceOne upsert idempotent (re-run ≡ one run). Carried-over ObjectId ids keep
    /// the surrogate-keyed collections stable, and business/natural keys keep the rest stable.
    /// </summary>
    public static IEnumerable<object[]> Cases()
    {
        yield return ["data", (ILegacyCollectionMapper)new IncidentMapper(Mapping.Clock),
            Fixtures.Load("data", "incident_happy")];
        yield return ["history", new IncidentHistoryMapper(Mapping.Clock),
            BsonDocument.Parse("""{ "_id": {"$oid":"5f1d7f0a9b2c3d4e5f6a7b91"}, "incidentId": "i", "created": {"$date":"2023-07-01T12:00:00Z"}, "man": 3 }""")];
        yield return ["weatherData", new WeatherHourlyMapper(Mapping.Clock),
            BsonDocument.Parse("""{ "_id": {"$oid":"5f1d7f0a9b2c3d4e5f6a7b92"}, "stationId": "560", "date": {"$date":"2023-07-01T12:00:00Z"}, "temperatura": 30 }""")];
        yield return ["weatherStations", new WeatherStationMapper(),
            BsonDocument.Parse("""{ "stationId": 579, "coordinates": [-9.14, 38.72], "location": "L" }""")];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Mapping_twice_yields_identical_bson(string _, ILegacyCollectionMapper mapper, BsonDocument doc)
    {
        ClassMaps.EnsureRegistered();

        var first = Serialize(mapper.Map(doc));
        var second = Serialize(mapper.Map(doc));

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
            Assert.Equal(first[i], second[i]); // BsonDocument value-equality
    }

    private static List<BsonDocument> Serialize(MapResult result)
    {
        var mapped = Assert.IsType<MapResult.Mapped>(result);
        return mapped.Entities.Select(e => e.Entity.ToBsonDocument(e.Entity.GetType())).ToList();
    }
}

public class QuarantineRecordTests
{
    [Fact]
    public void Quarantine_record_carries_reason_and_original_doc()
    {
        var original = BsonDocument.Parse("""{ "_id": {"$oid":"5f1d7f0a9b2c3d4e5f6a7b93"}, "junk": true }""");
        var record = ImportRunner.QuarantineDocument("data", "no business id", original);

        Assert.Equal("data", record["legacyCollection"].AsString);
        Assert.Equal("no business id", record["reason"].AsString);
        Assert.Equal(original, record["doc"].AsBsonDocument);
        Assert.True(record.Contains("importedAt"));
    }

    [Fact]
    public void Quarantined_result_reason_is_populated()
    {
        var mapper = new IncidentMapper(Mapping.Clock);
        var doc = Fixtures.Load("data", "incident_no_business_key");
        var q = Assert.IsType<MapResult.Quarantined>(mapper.Map(doc));
        Assert.False(string.IsNullOrWhiteSpace(q.Reason));
    }
}
