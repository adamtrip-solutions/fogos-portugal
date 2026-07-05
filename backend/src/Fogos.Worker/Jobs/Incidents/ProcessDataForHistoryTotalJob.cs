using Fogos.Domain.Incidents;
using Fogos.Domain.Stats;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Incidents;

/// <summary>
/// Ports <c>ProcessDataForHistoryTotal</c> (every 2 min): sums man/terrain/aerial over active fires with
/// statusCode ∈ {3,4,5,6}, and appends a <c>history_totals</c> row only when the numbers differ from the
/// latest one (legacy change-only semantics).
/// </summary>
public sealed class ProcessDataForHistoryTotalJob(
    ISingleFlightLock lockService,
    ILogger<ProcessDataForHistoryTotalJob> logger,
    MongoContext mongo,
    IClock clock) : UniqueJob(lockService, logger)
{
    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        var f = Builders<Incident>.Filter;
        var active = await mongo.Incidents
            .Find(f.Eq(x => x.Active, true) & f.Eq(x => x.Kind, IncidentKind.Fire)
                  & f.In(x => x.Status.Code, IncidentStatusCatalog.ActiveCodes))
            .ToListAsync(ct);

        var man = active.Sum(i => i.Resources.Man);
        var aerial = active.Sum(i => i.Resources.Aerial);
        var terrain = active.Sum(i => i.Resources.Terrain);
        var total = active.Count;

        var last = await mongo.HistoryTotals
            .Find(Builders<HistoryTotal>.Filter.Empty)
            .SortByDescending(x => x.At)
            .FirstOrDefaultAsync(ct);

        if (last is not null && last.Man == man && last.Aerial == aerial && last.Terrain == terrain && last.Total == total)
            return; // unchanged — skip the append (legacy semantics)

        await mongo.HistoryTotals.InsertOneAsync(new HistoryTotal
        {
            At = clock.UtcNow,
            Man = man,
            Aerial = aerial,
            Terrain = terrain,
            Total = total,
        }, cancellationToken: ct);

        logger.LogDebug("HistoryTotal appended: man={Man} terrain={Terrain} aerial={Aerial} fires={Total}", man, terrain, aerial, total);
    }
}
