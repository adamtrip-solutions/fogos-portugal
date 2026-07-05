using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Queue;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Detects rekindles on two triggers and flags the incident's <c>Signals</c> subdocument idempotently
/// (an atomic conditional <c>$set</c> claims the flag; a redelivery that finds it already set neither
/// re-writes nor re-dispatches):
/// <list type="bullet">
/// <item><b>Status regression</b> — a fire dropping back to "Em Curso" from 7/8/9 (STATUS_REGRESSION).</item>
/// <item><b>Proximity</b> — a new fire within <c>ProximityKm</c> of a fire closed (8/9/10) whose last
/// status change is within <c>ProximityWindowHours</c> (PROXIMITY, referencing that prior incident).</item>
/// </list>
/// </summary>
public sealed class RekindleHandler(
    MongoContext mongo,
    IClock clock,
    IEventDispatcher dispatcher,
    IOptions<SignalsOptions> options)
    : IEventHandler<IncidentStatusChanged>, IEventHandler<IncidentCreated>
{
    private static readonly int[] ClosedFireCodes =
        [IncidentStatusCatalog.Conclusao, IncidentStatusCatalog.Vigilancia, IncidentStatusCatalog.Encerrada];

    public async Task HandleAsync(IncidentStatusChanged evt, CancellationToken ct)
    {
        if (!SignalRules.IsStatusRegression(evt.PreviousCode, evt.CurrentCode))
            return;

        var incident = await Fetch(evt.IncidentId, ct);
        if (incident is null || incident.Kind != IncidentKind.Fire)
            return;

        if (await TryClaimAsync(evt.IncidentId, RekindleDetected.StatusRegression, priorIncidentId: null, ct))
            await dispatcher.DispatchAsync(
                new RekindleDetected(evt.IncidentId, null, RekindleDetected.StatusRegression), ct: ct);
    }

    public async Task HandleAsync(IncidentCreated evt, CancellationToken ct)
    {
        var incident = await Fetch(evt.IncidentId, ct);
        if (incident is null || incident.Kind != IncidentKind.Fire || incident.Coordinates is not { } point)
            return;

        var opts = options.Value;
        var maxDistanceMeters = opts.ProximityKm * 1000;

        // Nearest-first closed fires within the radius (excluding self); $near sorts by distance ascending.
        var nearFilter = new BsonDocument
        {
            ["coordinates"] = new BsonDocument("$near", new BsonDocument
            {
                ["$geometry"] = new BsonDocument
                {
                    ["type"] = "Point",
                    ["coordinates"] = new BsonArray { point.Longitude, point.Latitude },
                },
                ["$maxDistance"] = maxDistanceMeters,
            }),
            ["kind"] = IncidentKind.Fire.ToString(),
            ["status.code"] = new BsonDocument("$in", new BsonArray(ClosedFireCodes)),
            ["_id"] = new BsonDocument("$ne", evt.IncidentId),
        };

        var candidates = await mongo.Incidents
            .Find(new BsonDocumentFilterDefinition<Incident>(nearFilter))
            .Limit(20)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return;

        // Last status change per candidate, from the status-history log.
        var candidateIds = candidates.Select(c => c.Id).ToList();
        var lastChange = await LastStatusChangeAsync(candidateIds, ct);
        var cutoff = clock.UtcNow - TimeSpan.FromHours(opts.ProximityWindowHours);

        var prior = candidates.FirstOrDefault(c => lastChange.TryGetValue(c.Id, out var at) && at >= cutoff);
        if (prior is null)
            return;

        if (await TryClaimAsync(evt.IncidentId, RekindleDetected.Proximity, prior.Id, ct))
            await dispatcher.DispatchAsync(
                new RekindleDetected(evt.IncidentId, prior.Id, RekindleDetected.Proximity), ct: ct);
    }

    /// <summary>
    /// Atomically claims a rekindle <paramref name="kind"/> only if that kind has not already fired for the
    /// incident (per-kind so status regression and proximity each dispatch at most once, independently).
    /// Returns true for the single caller that wins. Sets the <c>Rekindle</c> bool whenever any kind lands;
    /// <c>RekindleOfId</c> is written only on the proximity path (<paramref name="priorIncidentId"/> non-null)
    /// and, since that kind is claimed once, is never overwritten.
    /// </summary>
    private async Task<bool> TryClaimAsync(string incidentId, string kind, string? priorIncidentId, CancellationToken ct)
    {
        var filter = Builders<Incident>.Filter.Eq(x => x.Id, incidentId)
                     & Builders<Incident>.Filter.Not(Builders<Incident>.Filter.AnyEq(x => x.Signals!.RekindleKinds, kind));
        var update = Builders<Incident>.Update
            .AddToSet(x => x.Signals!.RekindleKinds, kind)
            .Set(x => x.Signals!.Rekindle, true)
            .Set(x => x.Signals!.RekindleDetectedAt, clock.UtcNow);
        if (priorIncidentId is not null)
            update = update.Set(x => x.Signals!.RekindleOfId, priorIncidentId);

        var result = await mongo.Incidents.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    private async Task<IReadOnlyDictionary<string, DateTimeOffset>> LastStatusChangeAsync(
        IReadOnlyList<string> incidentIds, CancellationToken ct)
    {
        var rows = await mongo.IncidentStatusHistory
            .Find(Builders<IncidentStatusChange>.Filter.In(x => x.IncidentId, incidentIds))
            .Sort(Builders<IncidentStatusChange>.Sort.Descending(x => x.At))
            .ToListAsync(ct);

        var map = new Dictionary<string, DateTimeOffset>();
        foreach (var row in rows)
            map.TryAdd(row.IncidentId, row.At); // desc sort → first seen per incident is the latest
        return map;
    }

    private Task<Incident?> Fetch(string id, CancellationToken ct) =>
        mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(ct)!;
}
