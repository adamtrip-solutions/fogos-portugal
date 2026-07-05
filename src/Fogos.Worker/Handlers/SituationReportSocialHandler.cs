using Fogos.Domain.Events;
using Fogos.Domain.Reports;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Queue;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// On <see cref="SituationReportCreated"/>, posts the report body to Twitter/Facebook/Telegram (publishers
/// default to DryRun). An at-most-once <see cref="IProcessedMarker"/> claim per report id guards the
/// fan-out so at-least-once redelivery can't double-post — mirroring the summaries pattern.
/// </summary>
public sealed class SituationReportSocialHandler(
    MongoContext mongo,
    IProcessedMarker processed,
    ITwitterPublisher twitter,
    ITelegramPublisher telegram,
    IFacebookPublisher facebook)
    : IEventHandler<SituationReportCreated>
{
    public async Task HandleAsync(SituationReportCreated evt, CancellationToken ct)
    {
        if (!await processed.TryMarkAsync($"sitrepsocial:{evt.ReportId}", ct))
            return;

        var report = await mongo.SituationReports
            .Find(Builders<SituationReport>.Filter.Eq(x => x.Id, evt.ReportId))
            .FirstOrDefaultAsync(ct);
        if (report is null)
            return;

        var post = new SocialPost { Text = report.Body };
        await twitter.PublishAsync(post, ct: ct);
        await facebook.PublishAsync(post, ct: ct);
        await telegram.PublishAsync(post, ct: ct);
    }
}
