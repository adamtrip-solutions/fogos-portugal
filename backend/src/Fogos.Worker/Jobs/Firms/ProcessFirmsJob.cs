using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Scheduling;
using Fogos.Infrastructure.Sources;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Fogos.Worker.Jobs.Firms;

/// <summary>
/// NASA FIRMS thermal-hotspot ingest, every 15 minutes. When no FIRMS key is configured it skips with a
/// single ops Info (not per-incident spam). Single-flight across the fleet; failures escalate to ops and
/// never crash the scheduler.
/// </summary>
public sealed class ProcessFirmsJob(
    ISingleFlightLock lockService,
    ILogger<ProcessFirmsJob> logger,
    FirmsProcessor processor,
    IOptions<FogosSourcesOptions> sources,
    JobFreshness freshness,
    IOpsNotifier ops) : UniqueJob(lockService, logger)
{
    public const string FreshnessJob = "firms";
    private static readonly TimeSpan Cadence = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteCoreAsync(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        if (string.IsNullOrWhiteSpace(sources.Value.Firms.Key))
        {
            await ops.InfoAsync("🛰️ FIRMS skipped: NASA_FIRMS_KEY not configured.", ct);
            return;
        }

        await freshness.CheckStaleAsync(FreshnessJob, Cadence, ct);

        try
        {
            var count = await processor.ProcessAsync(ct);
            logger.LogInformation("FIRMS processed {Count} active fire incidents.", count);
            await freshness.MarkSuccessAsync(FreshnessJob, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FIRMS job failed.");
            await ops.ErrorAsync($"🛰️ FIRMS job failed: {ex.Message}", ct);
        }
    }
}
