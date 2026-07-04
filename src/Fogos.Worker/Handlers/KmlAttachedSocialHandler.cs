using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Rendering;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Announces an operator-attached KML perimeter (<see cref="KmlAttached"/>): a detail-page screenshot and a
/// "Nova área de interesse por @VostPT" tweet threaded onto the incident's social thread, dry-run by default.
/// Ports the default branch of <c>IncidentController::addKML</c>. The screenshot never blocks the post — on
/// renderer failure the tweet still goes out text-only. Re-fetches the incident first. The legacy VOST
/// second-account path (branch A) is not ported: only the main account is live (owner decision).
/// </summary>
public sealed class KmlAttachedSocialHandler(
    MongoContext mongo,
    SocialThreadStore threads,
    ITwitterPublisher twitter,
    RendererClient renderer,
    IOptions<IncidentPipelineOptions> options)
    : IEventHandler<KmlAttached>
{
    private string Domain => options.Value.SocialLinkDomain;

    public async Task HandleAsync(KmlAttached evt, CancellationToken ct)
    {
        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, evt.IncidentId))
            .FirstOrDefaultAsync(ct);
        if (incident is null)
            return;

        var text = IncidentCopy.KmlPerimeter(incident, Domain);
        var shot = await renderer.CaptureIncidentDetailAsync(incident.Id, width: 1200, height: 550, ct: ct);

        var thread = await threads.GetAsync(incident.Id, ct);
        var tweet = await twitter.PublishAsync(
            new SocialPost { Text = text, ImageBytes = shot, ReplyToId = thread?.LastTweetId }, ct: ct);

        if (tweet.Success && tweet.ExternalId is not null)
            await threads.SetLastTweetIdAsync(incident.Id, tweet.ExternalId, ct);
    }
}
