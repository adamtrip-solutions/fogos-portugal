using Fogos.Domain.Warnings;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>
/// Read queries for legacy broadcast warnings. The write channel is gone (avisos are automatic-only now),
/// but the collection is still populated by the legacy importer and read by the RSS warnings feed.
/// </summary>
public sealed class WarningReads(MongoContext context)
{
    public async Task<IReadOnlyList<Warning>> LatestAsync(WarningKind? kind, int limit, CancellationToken ct = default)
    {
        var filter = kind is { } k
            ? Builders<Warning>.Filter.Eq(x => x.Kind, k)
            : Builders<Warning>.Filter.Empty;
        return await context.Warnings
            .Find(filter)
            .Sort(Builders<Warning>.Sort.Descending(x => x.CreatedAt))
            .Limit(limit)
            .ToListAsync(ct);
    }
}
