namespace Fogos.Importer.Mapping;

/// <summary>One new-schema entity destined for a target collection.</summary>
/// <param name="Collection">Target Mongo collection name (as in <see cref="Fogos.Infrastructure.Mongo.MongoContext"/>).</param>
/// <param name="Entity">A domain entity; serialized via the registered class maps at upsert time.</param>
public sealed record MappedEntity(string Collection, object Entity);

/// <summary>
/// The pure outcome of mapping one legacy <c>BsonDocument</c>. Exactly one of the three
/// shapes. Mappers do no I/O — this is what the golden tests exercise directly.
/// </summary>
public abstract record MapResult
{
    private MapResult() { }

    /// <summary>Successfully mapped to one or more target entities (e.g. incident + social thread).</summary>
    public sealed record Mapped(IReadOnlyList<MappedEntity> Entities) : MapResult;

    /// <summary>Deliberately not ported (dead/superseded data). Counted, never written.</summary>
    public sealed record Skipped(string Reason) : MapResult;

    /// <summary>Fits no mapping rule. The original doc + reason land in <c>import_quarantine</c>.</summary>
    public sealed record Quarantined(string Reason) : MapResult;

    public static MapResult Map(params MappedEntity[] entities) => new Mapped(entities);
    public static MapResult Skip(string reason) => new Skipped(reason);
    public static MapResult Quarantine(string reason) => new Quarantined(reason);
}
