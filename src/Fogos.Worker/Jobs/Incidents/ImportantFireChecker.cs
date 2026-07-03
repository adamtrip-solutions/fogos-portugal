using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Handlers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Worker.Jobs.Incidents;

/// <summary>
/// Ports <c>CheckImportantFireIncident</c> (ShouldBeUnique): active fires, statusCode 1–6, not yet
/// posted, aerial+terrain &gt; 15, older than 3h → mark <c>important</c>, fan out the "🔥 incêndio
/// importante" post (Twitter thread + Telegram + Facebook) and the important push, and record
/// SentImportantPost so it fires once. Guarded by a Redis single-flight lock (the ShouldBeUnique twin).
/// </summary>
public sealed class ImportantFireChecker(
    MongoContext mongo,
    SocialThreadStore threads,
    ISingleFlightLock locks,
    IClock clock,
    ITwitterPublisher twitter,
    ITelegramPublisher telegram,
    IFacebookPublisher facebook,
    NotificationScheduler scheduler,
    FcmNotifier fcm,
    IOptions<IncidentPipelineOptions> options,
    ILogger<ImportantFireChecker> logger)
{
    private const string LockKey = "check-important";
    private string Domain => options.Value.SocialLinkDomain;

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var token = await locks.TryAcquireAsync(LockKey, TimeSpan.FromMinutes(5), ct);
        if (token is null)
        {
            logger.LogInformation("CheckImportant skipped: another run holds the lock.");
            return 0;
        }

        try
        {
            return await RunCoreAsync(ct);
        }
        finally
        {
            await locks.ReleaseAsync(LockKey, token, CancellationToken.None);
        }
    }

    private async Task<int> RunCoreAsync(CancellationToken ct)
    {
        var f = Builders<Incident>.Filter;
        var candidates = await mongo.Incidents
            .Find(f.Eq(x => x.Active, true) & f.Eq(x => x.Kind, IncidentKind.Fire) & f.Eq(x => x.Important, false))
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var posted = 0;

        foreach (var incident in candidates)
        {
            if (ct.IsCancellationRequested)
                break;
            if (!IncidentRules.QualifiesAsImportant(incident, now))
                continue;

            var thread = await threads.GetAsync(incident.Id, ct);
            if (thread?.SentImportantPost == true)
                continue;

            // Mark first (once-only across redelivery/concurrent runs), then fan out.
            await threads.MarkImportantSentAsync(incident.Id, ct);
            await mongo.Incidents.UpdateOneAsync(f.Eq(x => x.Id, incident.Id),
                Builders<Incident>.Update.Set(x => x.Important, true), cancellationToken: ct);

            await scheduler.ScheduleAsync("important", incident.Id, incident.Location,
                IncidentCopy.ImportantPush(incident),
                fcm.Topics.Incident(incident.Id, includeImportant: true).ToArray(), ct: ct);

            var tweet = await twitter.PublishAsync(
                new SocialPost { Text = IncidentCopy.ImportantTweet(incident, Domain), ReplyToId = thread?.LastTweetId }, ct: ct);
            if (tweet.Success && tweet.ExternalId is not null)
                await threads.SetLastTweetIdAsync(incident.Id, tweet.ExternalId, ct);

            await telegram.PublishAsync(new SocialPost { Text = IncidentCopy.ImportantTweet(incident, Domain) }, ct: ct);
            await facebook.PublishAsync(new SocialPost { Text = IncidentCopy.ImportantFacebook(incident, Domain) }, ct: ct);
            posted++;
        }

        logger.LogInformation("CheckImportant: {Posted} important fire posts.", posted);
        return posted;
    }
}
