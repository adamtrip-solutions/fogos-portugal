using Fogos.Domain.Reports;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>Read queries over persisted situation reports (newest first).</summary>
public sealed class SituationReportReads(MongoContext context)
{
    public async Task<IReadOnlyList<SituationReport>> LatestAsync(int first, CancellationToken ct = default) =>
        await context.SituationReports
            .Find(Builders<SituationReport>.Filter.Empty)
            .Sort(Builders<SituationReport>.Sort.Descending(x => x.At))
            .Limit(first)
            .ToListAsync(ct);

    public async Task<SituationReport?> LatestOneAsync(CancellationToken ct = default) =>
        await context.SituationReports
            .Find(Builders<SituationReport>.Filter.Empty)
            .Sort(Builders<SituationReport>.Sort.Descending(x => x.At))
            .FirstOrDefaultAsync(ct);
}
