using Fogos.Domain.Incidents;
using Fogos.Domain.Stats;
using Fogos.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>Aggregate read queries backing the <c>stats</c> field. Time windows are computed by the caller (Lisbon-local).</summary>
public sealed class StatsReads(MongoContext context)
{
    public async Task<int> ActiveCountAsync(bool fire, CancellationToken ct = default)
    {
        var f = Builders<Incident>.Filter;
        var kind = fire
            ? f.Eq(x => x.Kind, IncidentKind.Fire)
            : f.Ne(x => x.Kind, IncidentKind.Fire);
        return (int)await context.Incidents.CountDocumentsAsync(f.Eq(x => x.Active, true) & kind, cancellationToken: ct);
    }

    /// <summary>Fire ignitions whose occurrence falls in [from, to).</summary>
    public async Task<int> IgnitionCountAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var f = Builders<Incident>.Filter;
        var filter = f.Eq(x => x.Kind, IncidentKind.Fire)
                     & f.Gte(x => x.OccurredAt, from)
                     & f.Lt(x => x.OccurredAt, to);
        return (int)await context.Incidents.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    /// <summary>Occurrence instants of fire ignitions in [from, to) — caller buckets by Lisbon hour.</summary>
    public async Task<IReadOnlyList<DateTimeOffset>> IgnitionOccurredAtsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var f = Builders<Incident>.Filter;
        var filter = f.Eq(x => x.Kind, IncidentKind.Fire)
                     & f.Gte(x => x.OccurredAt, from)
                     & f.Lt(x => x.OccurredAt, to);
        var items = await context.Incidents
            .Find(filter)
            .Project(x => x.OccurredAt)
            .ToListAsync(ct);
        return items;
    }

    public async Task<HistoryTotal?> LatestTotalsAsync(CancellationToken ct = default) =>
        await context.HistoryTotals
            .Find(Builders<HistoryTotal>.Filter.Empty)
            .Sort(Builders<HistoryTotal>.Sort.Descending(x => x.At))
            .FirstOrDefaultAsync(ct);

    /// <summary>Sum of <c>icnf.burnArea.total</c> for fires occurring in [from, to).</summary>
    public async Task<double> BurnAreaTotalHaAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument
            {
                { "kind", IncidentKind.Fire.ToString() },
                { "occurredAt", new BsonDocument { { "$gte", from.UtcDateTime }, { "$lt", to.UtcDateTime } } },
                { "icnf.burnArea.total", new BsonDocument("$ne", BsonNull.Value) },
            }),
            new("$group", new BsonDocument { { "_id", BsonNull.Value }, { "total", new BsonDocument("$sum", "$icnf.burnArea.total") } }),
        };
        var row = await context.Incidents
            .Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
            .FirstOrDefaultAsync(ct);
        return row is null ? 0 : row["total"].ToDouble();
    }
}
