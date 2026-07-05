using Fogos.Domain.Warnings;
using Fogos.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>Read queries for broadcast warnings.</summary>
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

    public async Task<Warning?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;
        return await context.Warnings.Find(Builders<Warning>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(ct);
    }
}
