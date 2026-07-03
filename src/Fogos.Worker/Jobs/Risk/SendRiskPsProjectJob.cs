using Fogos.Domain.Risk;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Risk;

/// <summary>
/// Daily 08:30 Lisbon push of the configured project concelho's "today" fire risk to its Telegram
/// channel (legacy <c>SendRiskPSProject</c>). Dry-run by default. No configured DICO / no risk row is a
/// benign skip with a single ops Info; nothing here crashes the scheduler.
/// </summary>
public sealed class SendRiskPsProjectJob(
    ISingleFlightLock lockService,
    ILogger<SendRiskPsProjectJob> logger,
    MongoContext mongo,
    ITelegramPublisher telegram,
    IOptions<RiskProjectOptions> options,
    JobFreshness freshness,
    IOpsNotifier ops) : UniqueJob(lockService, logger)
{
    public const string FreshnessJob = "risk-ps-project";

    protected override async Task ExecuteCoreAsync(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.Dico))
        {
            await ops.InfoAsync("SendRiskPsProject skipped: no project DICO configured.", ct);
            return;
        }

        try
        {
            var latest = await mongo.RcmDaily
                .Find(Builders<ConcelhoRisk>.Filter.Eq(x => x.Dico, opts.Dico))
                .Sort(Builders<ConcelhoRisk>.Sort.Descending(x => x.Date))
                .FirstOrDefaultAsync(ct);

            if (latest?.Today is not { } level)
            {
                await ops.InfoAsync($"SendRiskPsProject: no RCM 'today' risk for DICO {opts.Dico}.", ct);
                return;
            }

            var post = new SocialPost
            {
                Text = RiskPostComposer.ProjectRiskToday(level),
                TelegramThreadId = opts.TelegramThreadId,
            };
            await telegram.PublishAsync(post, opts.TelegramChannelKey, ct);
            await freshness.MarkSuccessAsync(FreshnessJob, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendRiskPsProject failed.");
            await ops.ErrorAsync($"🔥 SendRiskPsProject failed: {ex.Message}", ct);
        }
    }
}
