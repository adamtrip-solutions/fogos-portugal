using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Rendering;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Ports the social triggers of <c>ProcessICNFFireData</c> (dry-run): on first-seen cause/source it posts
/// the cause info (+ incident push); on first KML it posts "🗺 Area ardida disponível" with a perimeter
/// screenshot; on a first, notable burn area (&gt; 0.5 ha) it posts the total. All threaded via
/// lastTweetId. Re-fetches the incident so it reads the merged <c>icnf</c> sub-document.
/// </summary>
public sealed class IcnfSocialHandler(
    MongoContext mongo,
    SocialThreadStore threads,
    ITwitterPublisher twitter,
    ITelegramPublisher telegram,
    IFacebookPublisher facebook,
    RendererClient renderer,
    NotificationScheduler scheduler,
    FcmNotifier fcm,
    IOptions<IncidentPipelineOptions> options)
    : IEventHandler<IcnfEnriched>
{
    private string Domain => options.Value.SocialLinkDomain;

    public async Task HandleAsync(IcnfEnriched evt, CancellationToken ct)
    {
        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, evt.IncidentId))
            .FirstOrDefaultAsync(ct);
        if (incident is null)
            return;

        if (evt.FirstCause)
            await PostCauseAsync(incident, ct);

        if (evt.FirstKml)
            await PostWithShotAsync(incident, IncidentCopy.IcnfKml(incident, Domain), facebookText: true, ct);

        if (evt.FirstBurnArea && incident.Icnf?.BurnArea?.Total is > 0.5)
            await PostWithShotAsync(incident, IncidentCopy.IcnfBurnArea(incident, Domain, incident.Icnf.BurnArea.Total!.Value), facebookText: false, ct);
    }

    private async Task PostCauseAsync(Incident incident, CancellationToken ct)
    {
        var hasCause = !string.IsNullOrEmpty(incident.Icnf?.Cause);
        var hasSource = !string.IsNullOrEmpty(incident.Icnf?.AlertSource);
        var text = (hasCause, hasSource) switch
        {
            (true, true) => IncidentCopy.IcnfCauseAndSource(incident, Domain),
            (true, false) => IncidentCopy.IcnfCause(incident, Domain),
            (false, true) => IncidentCopy.IcnfSource(incident, Domain),
            _ => null,
        };
        if (text is null)
            return;

        var thread = await threads.GetAsync(incident.Id, ct);
        var tweet = await twitter.PublishAsync(new SocialPost { Text = text, ReplyToId = thread?.LastTweetId }, ct: ct);
        if (tweet.Success && tweet.ExternalId is not null)
            await threads.SetLastTweetIdAsync(incident.Id, tweet.ExternalId, ct);

        await telegram.PublishAsync(new SocialPost { Text = text }, ct: ct);
        await facebook.PublishAsync(new SocialPost { Text = text }, ct: ct);

        // Incident-scoped push (legacy NotificationTool::send to the incident topic).
        await scheduler.ScheduleAsync("icnf-cause", incident.Id, incident.Location, text,
            fcm.Topics.Incident(incident.Id, includeImportant: false).ToArray(), ct: ct);
    }

    private async Task PostWithShotAsync(Incident incident, string text, bool facebookText, CancellationToken ct)
    {
        var shot = await renderer.CaptureIncidentDetailAsync(incident.Id, ct: ct);
        var thread = await threads.GetAsync(incident.Id, ct);

        var tweet = await twitter.PublishAsync(new SocialPost { Text = text, ImageBytes = shot, ReplyToId = thread?.LastTweetId }, ct: ct);
        if (tweet.Success && tweet.ExternalId is not null)
            await threads.SetLastTweetIdAsync(incident.Id, tweet.ExternalId, ct);

        await telegram.PublishAsync(new SocialPost { Text = text, ImageBytes = shot }, ct: ct);
        if (facebookText)
            await facebook.PublishAsync(new SocialPost { Text = text }, ct: ct);
    }
}
