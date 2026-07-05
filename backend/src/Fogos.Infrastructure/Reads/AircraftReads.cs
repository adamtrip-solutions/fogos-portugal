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

    // ── Incident associations ──────────────────────────────────────────────────

    /// <summary>All aircraft links for the given incidents (active-first, then most-recently-seen).</summary>
    public async Task<IReadOnlyList<IncidentAircraftLink>> LinksByIncidentsAsync(IReadOnlyList<string> incidentIds, CancellationToken ct = default) =>
        await context.IncidentAircraft
            .Find(Builders<IncidentAircraftLink>.Filter.In(x => x.IncidentId, incidentIds))
            .Sort(Builders<IncidentAircraftLink>.Sort.Descending(x => x.Active).Descending(x => x.LastSeenAt))
            .ToListAsync(ct);

    /// <summary>Tracked-fleet rows by ICAO (for joining registration / name / kind onto links).</summary>
    public async Task<IReadOnlyDictionary<string, TrackedAircraft>> TrackedByIcaosAsync(IReadOnlyList<string> icaos, CancellationToken ct = default)
    {
        var rows = await context.TrackedAircraft
            .Find(Builders<TrackedAircraft>.Filter.In(x => x.Icao, icaos))
            .ToListAsync(ct);
        return rows.ToDictionary(x => x.Icao);
    }

    /// <summary>Most recent active incident id per ICAO (for <c>aircraft.currentIncidentId</c>).</summary>
    public async Task<IReadOnlyDictionary<string, string>> ActiveIncidentByIcaosAsync(IReadOnlyList<string> icaos, CancellationToken ct = default)
    {
        var rows = await context.IncidentAircraft
            .Find(Builders<IncidentAircraftLink>.Filter.In(x => x.Icao, icaos)
                  & Builders<IncidentAircraftLink>.Filter.Eq(x => x.Active, true))
            .Sort(Builders<IncidentAircraftLink>.Sort.Descending(x => x.LastSeenAt))
            .ToListAsync(ct);
        var map = new Dictionary<string, string>();
        foreach (var r in rows)
            map.TryAdd(r.Icao, r.IncidentId); // sorted desc → first seen is the most recent.
        return map;
    }

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
