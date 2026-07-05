using MongoDB.Bson;

namespace Fogos.Importer.Mapping;

/// <summary>
/// Maps one legacy collection's raw documents into new-schema entities. Pure and I/O-free:
/// the <see cref="ImportRunner"/> handles streaming, upserts, and quarantine writes.
/// </summary>
public interface ILegacyCollectionMapper
{
    /// <summary>Legacy (source) collection name this mapper reads.</summary>
    string Name { get; }

    /// <summary>Human-readable description of the target it writes to (for the report).</summary>
    string TargetDescription { get; }

    /// <summary>Map a single legacy document. Never throws for data reasons — returns Quarantine instead.</summary>
    MapResult Map(BsonDocument doc);
}
