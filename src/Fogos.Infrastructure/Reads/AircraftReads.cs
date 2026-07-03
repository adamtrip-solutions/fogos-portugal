using Fogos.Domain.Aircraft;
using Fogos.Infrastructure.Mongo;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>Read queries for the tracked fleet and their flight positions.</summary>
public sealed class AircraftReads(MongoContext context)
{
    public async Task<IReadOnlyList<TrackedAircraft>> TrackedAsync(CancellationToken ct = default) =>
        await context.TrackedAircraft
            .Find(Builders<TrackedAircraft>.Filter.Eq(x => x.Active, true))
            .Sort(Builders<TrackedAircraft>.Sort.Ascending(x => x.Registration))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FlightPosition>> TrackAsync(string icao, int limit, CancellationToken ct = default) =>
        await context.FlightPositions
            .Find(Builders<FlightPosition>.Filter.Eq(x => x.Icao, icao))
            .Sort(Builders<FlightPosition>.Sort.Descending(x => x.SampledAt))
            .Limit(limit)
            .ToListAsync(ct);

    /// <summary>Latest position per ICAO via a single sort+group aggregation (no N+1).</summary>
    public async Task<IReadOnlyDictionary<string, FlightPosition>> LatestPositionsByIcaosAsync(IReadOnlyList<string> icaos, CancellationToken ct = default)
    {
        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument("icao", new BsonDocument("$in", new BsonArray(icaos)))),
            new("$sort", new BsonDocument("sampledAt", -1)),
            new("$group", new BsonDocument { { "_id", "$icao" }, { "doc", new BsonDocument("$first", "$$ROOT") } }),
        };
        var rows = await context.FlightPositions
            .Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
            .ToListAsync(ct);
        return rows
            .Select(r => BsonSerializer.Deserialize<FlightPosition>(r["doc"].AsBsonDocument))
            .ToDictionary(p => p.Icao);
    }
}
