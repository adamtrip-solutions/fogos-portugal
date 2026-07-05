using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Queue;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Ports <c>SaveIncidentHistory</c>: on creation and every resources change (year ≥ 2022), appends an
/// <c>incident_history</c> snapshot, then applies the big-incident rule — fire + man ≥ 100 + not yet
/// posted → "grande mobilização" tweet/telegram/facebook + important push, marked once per incident.
/// Re-fetches the incident before acting. (Legacy POSIT/COS posts came from the disabled ANEPC-email
/// path and are not ported — that data isn't in the clean feed.)
/// </summary>
public sealed class IncidentHistoryHandler(
    MongoContext mongo,
    IClock clock,
    SocialThreadStore threads,
    ITwitterPublisher twitter,
    ITelegramPublisher telegram,
    IFacebookPublisher facebook,
    FcmNotifier fcm,
    IOptions<IncidentPipelineOptions> options)
    : IEventHandler<IncidentCreated>, IEventHandler<IncidentResourcesChanged>
{
    private string Domain => options.Value.SocialLinkDomain;

    public Task HandleAsync(IncidentCreated evt, CancellationToken ct) => ProcessAsync(evt.IncidentId, ct);

    public Task HandleAsync(IncidentResourcesChanged evt, CancellationToken ct) => ProcessAsync(evt.IncidentId, ct);

    private async Task ProcessAsync(string incidentId, CancellationToken ct)
    {
        var incident = await Fetch(incidentId, ct);
        if (incident is null || clock.ToLisbon(incident.OccurredAt).Year < IncidentRules.HistoryMinYear)
            return;

        await mongo.IncidentHistory.InsertOneAsync(new IncidentHistorySnapshot
        {
            IncidentId = incident.Id,
            At = clock.UtcNow,
            Man = incident.Resources.Man,
            Terrain = incident.Resources.Terrain,
            Aerial = incident.Resources.Aerial,
            Location = incident.Location,
        }, cancellationToken: ct);

        await MaybePostBigAsync(incident, ct);
    }

    private async Task MaybePostBigAsync(Incident incident, CancellationToken ct)
    {
        if (!IncidentRules.QualifiesAsBig(incident))
            return;

        var thread = await threads.GetAsync(incident.Id, ct);
        if (thread?.SentBigIncidentPost == true)
            return;

        // Mark first (at-most-once, survives redelivery) before firing the fan-out.
        await threads.MarkBigSentAsync(incident.Id, ct);

        await fcm.SendNotificationAsync("Ocorrência Importante", IncidentCopy.BigPush(incident),
            fcm.Topics.Important(), ct: ct);

        var hhmm = clock.LisbonNow.ToString("HH:mm");
        var text = IncidentCopy.BigPost(incident, Domain, hhmm);
        var replyTo = thread?.LastTweetId;

        var tweet = await twitter.PublishAsync(new SocialPost { Text = text, ReplyToId = replyTo }, ct: ct);
        if (tweet.Success && tweet.ExternalId is not null)
            await threads.SetLastTweetIdAsync(incident.Id, tweet.ExternalId, ct);

        await telegram.PublishAsync(new SocialPost { Text = text }, ct: ct);
        await facebook.PublishAsync(new SocialPost { Text = text }, ct: ct);
        // NOTE: legacy TwitterTool::retweetVost — VOST second-account path not ported (owner decision).
    }

    private Task<Incident?> Fetch(string id, CancellationToken ct) =>
        mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(ct)!;
}
