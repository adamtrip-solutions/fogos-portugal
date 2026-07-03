using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Scheduling;
using Fogos.Infrastructure.Sources;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Fogos.Worker.Jobs.Risk;

/// <summary>
/// IPMA fire-risk (RCM) ingest. Runs hourly with no social output, plus 09:00 (publish today's risk)
/// and 18:00 (publish tomorrow's risk) Lisbon-local — the social/tomorrow flags arrive via job data,
/// mirroring legacy <c>ProcessRCM(false)</c> / <c>(true)</c> / <c>(true,true)</c>. Single-flight
/// across the fleet; parse failures escalate to ops with their cause and never crash the scheduler.
/// </summary>
public sealed class ProcessRcmJob(
    ISingleFlightLock lockService,
    ILogger<ProcessRcmJob> logger,
    IpmaClient ipma,
    RcmProcessor processor,
    JobFreshness freshness,
    IOpsNotifier ops) : UniqueJob(lockService, logger)
{
    public const string FreshnessJob = "rcm";
    public const string PublishSocialKey = "publishSocial";
    public const string TomorrowKey = "tomorrow";

    private static readonly TimeSpan Cadence = TimeSpan.FromHours(1);

    protected override async Task ExecuteCoreAsync(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var publishSocial = context.MergedJobDataMap.GetBoolean(PublishSocialKey);
        var tomorrow = context.MergedJobDataMap.GetBoolean(TomorrowKey);

        await freshness.CheckStaleAsync(FreshnessJob, Cadence, ct);

        try
        {
            var page = await ipma.GetRcmPageAsync(ct);
            await processor.ProcessAsync(page, publishSocial, tomorrow, ct);
            await freshness.MarkSuccessAsync(FreshnessJob, ct);
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
