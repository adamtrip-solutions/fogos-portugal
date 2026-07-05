using Fogos.Domain.Events;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Scheduling;
using Fogos.Infrastructure.Sources;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Fogos.Worker.Jobs.Risk;

/// <summary>
/// IPMA fire-risk (RCM) ingest, hourly Lisbon-local. Single-flight across the fleet; parse failures
/// escalate to ops with their cause and never crash the scheduler.
/// </summary>
public sealed class ProcessRcmJob(
    ISingleFlightLock lockService,
    ILogger<ProcessRcmJob> logger,
    IpmaClient ipma,
    RcmProcessor processor,
    JobFreshness freshness,
    IEventDispatcher dispatcher,
    IOpsNotifier ops) : UniqueJob(lockService, logger)
{
    public const string FreshnessJob = "rcm";

    private static readonly TimeSpan Cadence = TimeSpan.FromHours(1);

    protected override async Task ExecuteCoreAsync(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        await freshness.CheckStaleAsync(FreshnessJob, Cadence, ct);

        try
        {
            var page = await ipma.GetRcmPageAsync(ct);
            var forecastDate = await processor.ProcessAsync(page, ct);
            await freshness.MarkSuccessAsync(FreshnessJob, ct);

            // Announce the daily-risk update so subscriptions with a risk threshold get matched.
            await dispatcher.DispatchAsync(new RcmProcessed(forecastDate), ct: ct);
        }
        catch (RcmParseException ex)
        {
            logger.LogError(ex, "RCM parse failed ({Failure}).", ex.Failure);
            await ops.ErrorAsync($"🔥 RCM parse failed ({ex.Failure}): {ex.Message}", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RCM job failed.");
            await ops.ErrorAsync($"🔥 RCM job failed: {ex.Message}", ct);
        }
    }
}
