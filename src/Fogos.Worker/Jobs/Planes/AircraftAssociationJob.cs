using Fogos.Domain.Aircraft;
using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Planes;

/// <summary>
/// Every 2 minutes, associates tracked aircraft with the active fire they are loitering over. For each
/// active fire with coordinates, an aircraft with at least <c>MinSamples</c> recent flight positions
/// whose latest fix is within <c>RadiusKm</c> is linked (upsert into <c>incident_aircraft</c>: bump
/// LastSeenAt / Samples, Active = true). When an aircraft matches several fires it is attached to the
/// nearest only. Links unseen for longer than <c>StaleMinutes</c> are deactivated. Single-flight.
/// </summary>
public sealed class AircraftAssociationJob(
    ISingleFlightLock lockService,
    ILogger<AircraftAssociationJob> logger,
    MongoContext mongo,
    IncidentReads incidents,
    IClock clock,
    IOptions<AircraftAssociationOptions> options) : UniqueJob(lockService, logger)
{
    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var now = clock.UtcNow;

        var fires = (await incidents.ActiveAsync([IncidentKind.Fire], ct))
            .Where(f => f.Coordinates is not null)
            .ToList();

        if (fires.Count > 0)
        {
            var positionsByIcao = await RecentPositionsAsync(now, opts, ct);
            var matches = NearestFirePerAircraft(fires, positionsByIcao, opts);
            foreach (var (icao, fireId) in matches)
                await UpsertLinkAsync(fireId, icao, now, ct);
        }

        await ExpireStaleAsync(now, opts, ct);
    }

    /// <summary>All recent flight positions grouped per ICAO (window fixes; per-fire proximity is judged later).</summary>
    private async Task<IReadOnlyDictionary<string, List<FlightPosition>>> RecentPositionsAsync(
        DateTimeOffset now, AircraftAssociationOptions opts, CancellationToken ct)
    {
        var cutoff = now - TimeSpan.FromMinutes(opts.LookbackMinutes);
        var recent = await mongo.FlightPositions
            .Find(Builders<FlightPosition>.Filter.Gte(x => x.SampledAt, cutoff))
            .ToListAsync(ct);

        return recent
            .GroupBy(p => p.Icao)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// For each aircraft, the nearest active fire it is genuinely loitering over: a fire counts only when at
    /// least <c>MinSamples</c> of the aircraft's window fixes fall within <c>RadiusKm</c> of it (so a
    /// transiting aircraft with a single near fix is not linked). Nearest-fire tiebreak by the latest fix.
    /// </summary>
    private static IReadOnlyList<(string Icao, string FireId)> NearestFirePerAircraft(
        IReadOnlyList<Incident> fires,
        IReadOnlyDictionary<string, List<FlightPosition>> positionsByIcao,
        AircraftAssociationOptions opts)
    {
        var matches = new List<(string, string)>();
        foreach (var (icao, positions) in positionsByIcao)
        {
            var latest = positions.MaxBy(p => p.SampledAt)!;
            string? bestFire = null;
            var bestDistance = double.MaxValue;
            foreach (var fire in fires)
            {
                var samplesNear = positions.Count(p => fire.Coordinates!.Value.DistanceKm(p.Position) <= opts.RadiusKm);
                if (samplesNear < opts.MinSamples)
                    continue;
                var distance = fire.Coordinates!.Value.DistanceKm(latest.Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestFire = fire.Id;
                }
            }
            if (bestFire is not null)
                matches.Add((icao, bestFire));
        }
        return matches;
    }

    private async Task UpsertLinkAsync(string fireId, string icao, DateTimeOffset now, CancellationToken ct)
    {
        var filter = Builders<IncidentAircraftLink>.Filter.Eq(x => x.IncidentId, fireId)
                     & Builders<IncidentAircraftLink>.Filter.Eq(x => x.Icao, icao);
        var update = Builders<IncidentAircraftLink>.Update
            .SetOnInsert(x => x.FirstSeenAt, now)
            .Set(x => x.LastSeenAt, now)
            .Set(x => x.Active, true)
            .Inc(x => x.Samples, 1);

        try
        {
            await mongo.IncidentAircraft.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, ct);

            // An aircraft belongs to one fire at a time: deactivate its active links to every OTHER incident
            // now, rather than waiting for the stale sweep to catch up.
            await mongo.IncidentAircraft.UpdateManyAsync(
                Builders<IncidentAircraftLink>.Filter.Eq(x => x.Icao, icao)
                    & Builders<IncidentAircraftLink>.Filter.Ne(x => x.IncidentId, fireId)
                    & Builders<IncidentAircraftLink>.Filter.Eq(x => x.Active, true),
                Builders<IncidentAircraftLink>.Update.Set(x => x.Active, false),
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Aircraft association upsert failed for incident {IncidentId} / {Icao}", fireId, icao);
        }
    }

    private async Task ExpireStaleAsync(DateTimeOffset now, AircraftAssociationOptions opts, CancellationToken ct)
    {
        var staleBefore = now - TimeSpan.FromMinutes(opts.StaleMinutes);
        var filter = Builders<IncidentAircraftLink>.Filter.Eq(x => x.Active, true)
                     & Builders<IncidentAircraftLink>.Filter.Lt(x => x.LastSeenAt, staleBefore);
        await mongo.IncidentAircraft.UpdateManyAsync(
            filter, Builders<IncidentAircraftLink>.Update.Set(x => x.Active, false), cancellationToken: ct);
    }
}
