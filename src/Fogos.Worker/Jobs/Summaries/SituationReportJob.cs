using System.Globalization;
using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Reports;
using Fogos.Domain.Time;
using Fogos.Domain.Warnings;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Summaries;

/// <summary>
/// Twice-daily (09:00 + 20:00 Lisbon) nationwide situation report. Composes from live data — active-fire
/// count, latest nationwide totals, top-5 active fires by mobilized assets, escalating count, warnings
/// issued in the last 12 h, and the year's accounted ICNF burn area — persists it, then dispatches
/// <see cref="SituationReportCreated"/> for social fan-out + webhook delivery. Single-flight (fleet posts
/// once); the slot is derived from the Lisbon hour so a single job serves both triggers.
/// </summary>
public sealed class SituationReportJob(
    ISingleFlightLock lockService,
    ILogger<SituationReportJob> logger,
    MongoContext mongo,
    IncidentReads incidents,
    StatsReads stats,
    IClock clock,
    IEventDispatcher dispatcher,
    IOpsNotifier ops) : UniqueJob(lockService, logger)
{
    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var now = clock.UtcNow;
            var lisbonNow = clock.LisbonNow;
            var slot = lisbonNow.Hour < 14 ? "morning" : "evening";

            var activeFires = await incidents.ActiveAsync([IncidentKind.Fire], ct);
            var activeCount = activeFires.Count;

            var totals = await stats.LatestTotalsAsync(ct);
            var man = totals?.Man ?? activeFires.Sum(i => i.Resources.Man);
            var terrain = totals?.Terrain ?? activeFires.Sum(i => i.Resources.Terrain);
            var aerial = totals?.Aerial ?? activeFires.Sum(i => i.Resources.Aerial);

            var top = activeFires
                .OrderByDescending(i => i.Resources.TotalAssets)
                .Take(5)
                .ToList();
            var escalating = activeFires.Count(i => i.Signals?.Escalating == true);

            var warnings12h = (int)await mongo.Warnings.CountDocumentsAsync(
                Builders<Warning>.Filter.Gte(x => x.CreatedAt, now - TimeSpan.FromHours(12)), cancellationToken: ct);

            var yearStart = clock.FromLisbon(new DateTime(clock.LisbonToday.Year, 1, 1, 0, 0, 0));
            var burnHa = (long)Math.Round(await stats.BurnAreaTotalHaAsync(yearStart, now, ct), MidpointRounding.AwayFromZero);

            var dateLabel = lisbonNow.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            var body = SituationReportCopy.Compose(
                slot, dateLabel, activeCount, man, terrain, aerial, escalating, warnings12h, burnHa,
                top.Select(i => (i.Concelho, i.Resources.TotalAssets)).ToList());

            var report = new SituationReport
            {
                At = now,
                Slot = slot,
                Body = body,
                ActiveFires = activeCount,
                TotalMan = man,
                TotalTerrain = terrain,
                TotalAerial = aerial,
                TopIncidentIds = top.Select(i => i.Id).ToList(),
            };

            await mongo.SituationReports.InsertOneAsync(report, cancellationToken: ct);
            await dispatcher.DispatchAsync(new SituationReportCreated(report.Id), ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SituationReport failed.");
            await ops.ErrorAsync($"🔥 SituationReport failed: {ex.Message}", ct);
        }
    }
}
