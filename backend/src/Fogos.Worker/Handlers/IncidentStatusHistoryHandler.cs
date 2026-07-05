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
/// Ports <c>SaveIncidentStatusHistory</c>: appends an <c>incident_status_history</c> row, comments the
/// transition on the stored Facebook post, and — for fires only — publishes the "🚨 Reacendimento" and
/// "✅ Dominado" transition posts (screenshot of <c>fogo/{id}/detalhe</c>, degrading to text-only on
/// failure; threaded tweet chained via lastTweetId; Telegram; Discord posts channel). Always schedules
/// the status-change push (3-minute delay). Re-fetches the incident before acting.
/// </summary>
public sealed class IncidentStatusHistoryHandler(
    MongoContext mongo,
    IClock clock,
    SocialThreadStore threads,
    ITwitterPublisher twitter,
    ITelegramPublisher telegram,
    IFacebookPublisher facebook,
    IDiscordPostPublisher discord,
    RendererClient renderer,
    NotificationScheduler scheduler,
    FcmNotifier fcm,
    IOptions<IncidentPipelineOptions> options)
    : IEventHandler<IncidentStatusChanged>
{
    private static readonly IReadOnlySet<int> ReacendimentoFrom =
        new HashSet<int> { IncidentStatusCatalog.Conclusao, IncidentStatusCatalog.EmResolucao, IncidentStatusCatalog.Vigilancia };

    private static readonly IReadOnlySet<int> DominadoTo =
        new HashSet<int> { IncidentStatusCatalog.Conclusao, IncidentStatusCatalog.EmResolucao };

    private string Domain => options.Value.SocialLinkDomain;

    public async Task HandleAsync(IncidentStatusChanged evt, CancellationToken ct)
    {
        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, evt.IncidentId))
            .FirstOrDefaultAsync(ct);

        if (incident is null || clock.ToLisbon(incident.OccurredAt).Year < IncidentRules.HistoryMinYear)
            return;

        var thread = await threads.GetAsync(incident.Id, ct);
        var hhmm = clock.LisbonNow.ToString("HH:mm");

        // Document the transition as a Facebook comment on the incident's original post (any incident).
        if (!string.IsNullOrEmpty(thread?.FacebookPostId))
            await facebook.CommentOnPostAsync(thread.FacebookPostId, IncidentCopy.StatusComment(hhmm, evt.PreviousLabel, evt.CurrentLabel), ct: ct);

        if (incident.Kind == IncidentKind.Fire)
        {
            if (evt.CurrentCode == IncidentStatusCatalog.EmCurso && ReacendimentoFrom.Contains(evt.PreviousCode))
                await PostTransitionAsync(incident, thread, IncidentCopy.Reacendimento(incident, Domain), toDiscord: true, toFacebook: true, ct);
            else if (DominadoTo.Contains(evt.CurrentCode) && evt.PreviousCode == IncidentStatusCatalog.EmCurso)
                await PostTransitionAsync(incident, thread, IncidentCopy.Dominado(incident, Domain), toDiscord: false, toFacebook: false, ct);
        }

        // Always: status-change push (delayed 3 min via the scheduler).
        await scheduler.ScheduleAsync(
            "status-change", incident.Id, incident.Location,
            IncidentCopy.StatusPush(evt.PreviousLabel, evt.CurrentLabel),
            fcm.Topics.Incident(incident.Id, includeImportant: false).ToArray(), ct: ct);

        await mongo.IncidentStatusHistory.InsertOneAsync(new IncidentStatusChange
        {
            IncidentId = incident.Id,
            At = clock.UtcNow,
            Code = incident.Status.Code,
            Label = incident.Status.Label,
        }, cancellationToken: ct);
    }

    private async Task PostTransitionAsync(Incident incident, Domain.Social.SocialThread? thread, string text, bool toDiscord, bool toFacebook, CancellationToken ct)
    {
        var shot = await renderer.CaptureIncidentDetailAsync(incident.Id, ct: ct); // null → text-only
        var post = new SocialPost { Text = text, ImageBytes = shot, ReplyToId = thread?.LastTweetId };

        var tweet = await twitter.PublishAsync(post, ct: ct);
        if (tweet.Success && tweet.ExternalId is not null)
            await threads.SetLastTweetIdAsync(incident.Id, tweet.ExternalId, ct);

        await telegram.PublishAsync(post, ct: ct);
        if (toFacebook)
            await facebook.PublishAsync(new SocialPost { Text = text, ImageBytes = shot }, ct: ct);
        if (toDiscord)
            await discord.PublishAsync(new SocialPost { Text = text }, ct: ct);
    }
}
