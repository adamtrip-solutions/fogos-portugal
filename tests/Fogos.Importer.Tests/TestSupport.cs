using Fogos.Domain.Time;
using Fogos.Importer.Mapping;
using Fogos.Infrastructure.Mongo;
using MongoDB.Bson;

namespace Fogos.Importer.Tests;

/// <summary>Registers the production class maps once so entity serialization matches the real importer.</summary>
public static class ClassMaps
{
    static ClassMaps() => FogosClassMaps.Register();

    /// <summary>Touch to trigger the static constructor.</summary>
    public static void EnsureRegistered() { }
}

/// <summary>Loads Extended-JSON fixture docs from <c>Fixtures/&lt;collection&gt;/&lt;name&gt;.json</c>.</summary>
public static class Fixtures
{
    public static BsonDocument Load(string collection, string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", collection, name);
        if (!path.EndsWith(".json", StringComparison.Ordinal))
            path += ".json";
        return BsonDocument.Parse(File.ReadAllText(path));
    }
}

/// <summary>Shared mapping helpers for the golden tests.</summary>
public static class Mapping
{
    /// <summary>A fixed clock is unnecessary: mappers only use the clock's Lisbon conversion, which is deterministic.</summary>
    public static readonly IClock Clock = new FogosClock();

    public static MapResult MapOne(ILegacyCollectionMapper mapper, BsonDocument doc)
    {
        ClassMaps.EnsureRegistered();
        return mapper.Map(doc);
    }

    /// <summary>Asserts the result is a single Mapped entity of type <typeparamref name="T"/> and returns it.</summary>
    public static T MappedSingle<T>(MapResult result) where T : class
    {
        var mapped = Assert.IsType<MapResult.Mapped>(result);
        var entity = Assert.Single(mapped.Entities, e => e.Entity is T);
        return (T)entity.Entity;
    }

    public static T? MappedOrNull<T>(MapResult result) where T : class
    {
        if (result is not MapResult.Mapped mapped) return null;
        return mapped.Entities.Select(e => e.Entity).OfType<T>().FirstOrDefault();
    }
}
