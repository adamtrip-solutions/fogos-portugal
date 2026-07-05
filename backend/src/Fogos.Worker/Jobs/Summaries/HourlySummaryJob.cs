using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Rendering;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Summaries;

/// <summary>
/// Hourly (minute 0, Lisbon) active-fire summary, ported from the legacy <c>HourlySummary</c> job. Totals the
/// means committed to fires currently active (count / operationals / vehicles / aerial), captures the
/// <c>estatisticas</c> overview screenshot, and posts to Twitter/Telegram (+Facebook when the screenshot
/// succeeds), dry-run by default. Zero active fires still posts the "Sem registo de incêndios ativos" notice,
/// mirroring legacy (no skip). The in-resolution block is driven by the same <c>active==true</c> flag as
/// legacy, differing only in status set — under the clean schema (active ⟺ codes 3–6) that group is empty,
/// so the "#Status" suffix is used. Bluesky is deleted (v5). Single-flight so overlapping fires run once.
/// </summary>
public sealed class HourlySummaryJob(
    ISingleFlightLock lockService,
    ILogger<HourlySummaryJob> logger,
    MongoContext mongo,
    IClock clock,
    ITwitterPublisher twitter,
    ITelegramPublisher telegram,
    IFacebookPublisher facebook,
    RendererClient renderer,
    IOpsNotifier ops) : UniqueJob(lockService, logger)
{
    /// <summary>The legacy overview page shot for the summary image (statistics page, phantom mode).</summary>
    public const string ScreenshotPath = "estatisticas?phantom=1";

    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var active = await SumGroupAsync(IncidentStatusCatalog.ActiveCodes, ct);
            var inResolution = await SumGroupAsync(IncidentStatusCatalog.InactiveCodes, ct);

            var hhmm = clock.LisbonNow.ToString("HH:mm");
            string status;
            if (active.Count == 0)
            {
                status = SummaryCopy.HourlyNoActive(hhmm);
            }
            else
            {
                status = SummaryCopy.HourlyActive(hhmm, active.Count, active.Man, active.Terrain, active.Aerial);
                status += inResolution.Count == 0
                    ? SummaryCopy.HourlyResolutionSuffixNone()
                    : SummaryCopy.HourlyResolutionSuffix(inResolution.Count, inResolution.Man, inResolution.Terrain, inResolution.Aerial);
            }

            // Screenshot never blocks the post: text-only when it fails (legacy $path === false path).
            var shot = await renderer.CaptureAsync(ScreenshotPath, width: 1200, height: 450, waitFor: null, ct: ct);

            await twitter.PublishAsync(new SocialPost { Text = status, ImageBytes = shot }, ct: ct);
            if (shot is not null)
                await facebook.PublishAsync(new SocialPost { Text = status, ImageBytes = shot }, ct: ct);
            await telegram.PublishAsync(new SocialPost { Text = status, ImageBytes = shot }, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HourlySummary failed.");
            await ops.ErrorAsync($"🔥 HourlySummary failed: {ex.Message}", ct);
        }
    }

    private async Task<GroupTotals> SumGroupAsync(IReadOnlySet<int> statusCodes, CancellationToken ct)
    {
        var f = Builders<Incident>.Filter;
        var filter = f.Eq(x => x.Active, true) & f.Eq(x => x.Kind, IncidentKind.Fire) & f.In(x => x.Status.Code, statusCodes);
        var fires = await mongo.Incidents.Find(filter).ToListAsync(ct);
        return new GroupTotals(
            fires.Count,
            fires.Sum(x => x.Resources.Man),
            fires.Sum(x => x.Resources.Terrain),
            fires.Sum(x => x.Resources.Aerial));
    }

    private readonly record struct GroupTotals(int Count, int Man, int Terrain, int Aerial);
}
