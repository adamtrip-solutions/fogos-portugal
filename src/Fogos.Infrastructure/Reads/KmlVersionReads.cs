using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>Metadata view of a stored KML perimeter version (never carries the raw KML — too big).</summary>
public sealed record KmlVersionMetaRow(string Id, string IncidentId, bool Vost, DateTimeOffset CapturedAt, int SizeBytes);

/// <summary>Read queries for versioned incident KML perimeters (metadata lists + single-version fetch).</summary>
public sealed class KmlVersionReads(MongoContext context)
{
    private static readonly SortDefinition<IncidentKmlVersion> NewestFirst =
        Builders<IncidentKmlVersion>.Sort.Descending(x => x.CapturedAt);

    /// <summary>Version metadata for one incident, newest first (no KML payload).</summary>
    public async Task<IReadOnlyList<KmlVersionMetaRow>> MetaByIncidentAsync(string incidentId, CancellationToken ct = default) =>
        await context.IncidentKmlVersions
            .Find(Builders<IncidentKmlVersion>.Filter.Eq(x => x.IncidentId, incidentId))
            .Sort(NewestFirst)
            .Project(x => new KmlVersionMetaRow(x.Id, x.IncidentId, x.Vost, x.CapturedAt, x.SizeBytes))
            .ToListAsync(ct);

    /// <summary>Version metadata for many incidents, newest first (batched for the DataLoader).</summary>
    public async Task<IReadOnlyList<KmlVersionMetaRow>> MetaByIncidentsAsync(IReadOnlyList<string> incidentIds, CancellationToken ct = default) =>
        await context.IncidentKmlVersions
            .Find(Builders<IncidentKmlVersion>.Filter.In(x => x.IncidentId, incidentIds))
            .Sort(NewestFirst)
            .Project(x => new KmlVersionMetaRow(x.Id, x.IncidentId, x.Vost, x.CapturedAt, x.SizeBytes))
            .ToListAsync(ct);

    /// <summary>Fetches one KML version (with its payload) scoped to its incident, or null when absent.</summary>
    public async Task<IncidentKmlVersion?> GetVersionAsync(string incidentId, string versionId, CancellationToken ct = default)
    {
        // The version id is an ObjectId _id; a malformed value would throw on serialization → guard to 404.
        if (!ObjectId.TryParse(versionId, out _))
            return null;
        return await context.IncidentKmlVersions
            .Find(Builders<IncidentKmlVersion>.Filter.Eq(x => x.Id, versionId)
                  & Builders<IncidentKmlVersion>.Filter.Eq(x => x.IncidentId, incidentId))
            .FirstOrDefaultAsync(ct);
    }
}
