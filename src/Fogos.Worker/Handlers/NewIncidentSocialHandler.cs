using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
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
/// Ports <c>HandleNewIncidentSocialMedia</c> (fires only, year ≥ 2022): the new-fire district push,
/// then the "🔥⚠ Novo incêndio" tweet with a detail screenshot (text-only on renderer failure),
/// Facebook photo post, and Telegram — seeding the incident's social thread (lastTweetId /
/// facebookPostId). Re-fetches the incident first.
/// NOTE: the legacy Emergencias/VOST second-account paths are NOT ported (owner decision: only the main
/// account is live). Bluesky is deleted (v5 decision).
/// </summary>
public sealed class NewIncidentSocialHandler(
    MongoContext mongo,
    IClock clock,
    SocialThreadStore threads,
    ITwitterPublisher twitter,
    ITelegramPublisher telegram,
    IFacebookPublisher facebook,
    RendererClient renderer,
    NotificationScheduler scheduler,
    FcmNotifier fcm,
    IOptions<IncidentPipelineOptions> options)
    : IEventHandler<IncidentCreated>
{
    private string Domain => options.Value.SocialLinkDomain;

    public async Task HandleAsync(IncidentCreated evt, CancellationToken ct)
    {
        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, evt.IncidentId))
            .FirstOrDefaultAsync(ct);

        if (incident is null
            || incident.Kind != IncidentKind.Fire
            || clock.ToLisbon(incident.OccurredAt).Year < IncidentRules.HistoryMinYear)
            return;

        // New-fire district push (NotificationTool::sendNewFireNotification, 3-min delayed).
        var nature = string.IsNullOrEmpty(incident.Natureza) ? "" : $" - {incident.Natureza}";
        await scheduler.ScheduleAsync(
            "new-fire", incident.Id, incident.Location,
            $"Novo incêndio em {incident.Location}{nature}",
            fcm.Topics.NewFire(incident.Dico, incident.District).ToArray(), ct: ct);

        var text = IncidentCopy.NewFire(incident, Domain);
        var shot = await renderer.CaptureIncidentDetailAsync(incident.Id, ct: ct);

        var tweet = await twitter.PublishAsync(new SocialPost { Text = text, ImageBytes = shot }, ct: ct);
        if (tweet.Success && tweet.ExternalId is not null)
            await threads.SetLastTweetIdAsync(incident.Id, tweet.ExternalId, ct);

        var fb = await facebook.PublishAsync(new SocialPost { Text = text, ImageBytes = shot }, ct: ct);
        if (fb.Success && fb.ExternalId is not null)
            await threads.SetFacebookPostIdAsync(incident.Id, fb.ExternalId, ct);

        await telegram.PublishAsync(new SocialPost { Text = text, ImageBytes = shot }, ct: ct);
    }
}
