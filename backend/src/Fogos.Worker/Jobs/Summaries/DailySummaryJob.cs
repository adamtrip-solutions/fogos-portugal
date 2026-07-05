using System.Globalization;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Summaries;

/// <summary>
/// Daily (09:30 Lisbon) recap of yesterday's fire activity, ported from the legacy <c>DailySummary</c> job.
/// Reports yesterday's ignition count, the peak means each fire mobilized (max across its history snapshots,
/// summed over the fires), and the accounted burn area, then posts to Twitter/Facebook/Telegram, dry-run by
/// default. Bluesky is deleted (v5); the legacy <c>retweetVost</c> second-account path is not ported (owner
/// decision). Single-flight so the fleet posts once.
/// </summary>
public sealed class DailySummaryJob(
    ISingleFlightLock lockService,
    ILogger<DailySummaryJob> logger,
    MongoContext mongo,
    IncidentReads incidents,
    StatsReads stats,
    IClock clock,
    ITwitterPublisher twitter,
    ITelegramPublisher telegram,
    IFacebookPublisher facebook,
    IOpsNotifier ops) : UniqueJob(lockService, logger)
{
    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var yesterday = clock.LisbonToday.AddDays(-1);
            var start = clock.FromLisbon(yesterday.ToDateTime(TimeOnly.MinValue));
            var end = clock.FromLisbon(clock.LisbonToday.ToDateTime(TimeOnly.MinValue));

            var f = Builders<Incident>.Filter;
            var window = f.Eq(x => x.Kind, IncidentKind.Fire) & f.Gte(x => x.OccurredAt, start) & f.Lt(x => x.OccurredAt, end);
            var ids = await mongo.Incidents.Find(window).Project(x => x.Id).ToListAsync(ct);
            var total = ids.Count;

            var (maxMan, maxCars, maxPlanes) = await PeakMeansAsync(ids, ct);
            var burnHa = (long)Math.Round(await stats.BurnAreaTotalHaAsync(start, end, ct), MidpointRounding.AwayFromZero);

            var date = yesterday.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            var status = SummaryCopy.Daily(date, total, maxMan, maxCars, maxPlanes, burnHa);

            var post = new SocialPost { Text = status };
            await twitter.PublishAsync(post, ct: ct);
            // NOTE: legacy TwitterTool::retweetVost($id) — VOST second-account path not ported (owner decision).
            await facebook.PublishAsync(post, ct: ct);
            await telegram.PublishAsync(post, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DailySummary failed.");
            await ops.ErrorAsync($"🔥 DailySummary failed: {ex.Message}", ct);
        }
    }

    /// <summary>Per-incident peak means (max across each fire's history snapshots), summed over the fires.</summary>
    private async Task<(int Man, int Cars, int Planes)> PeakMeansAsync(IReadOnlyList<string> incidentIds, CancellationToken ct)
    {
        if (incidentIds.Count == 0)
            return (0, 0, 0);

        var snapshots = await incidents.HistoryByIncidentsAsync(incidentIds, ct);
        int man = 0, cars = 0, planes = 0;
        foreach (var group in snapshots.GroupBy(s => s.IncidentId))
        {
            man += group.Max(s => s.Man);
            cars += group.Max(s => s.Terrain);
            planes += group.Max(s => s.Aerial);
        }
        return (man, cars, planes);
    }
}
