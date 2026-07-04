using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Ports <c>PhotoModerationController::broadcastApprovedPhoto</c>: on an approved+public photo, fetch the
/// stored JPEG and fan out a "new photo" post to Twitter / Facebook / Telegram / Discord (all dry-run by
/// default) plus the per-incident FCM push. Re-fetches the photo and incident first; a photo that is no
/// longer approved/public (e.g. rejected between dispatch and handling) is skipped.
/// </summary>
public sealed class ApprovedPhotoSocialHandler(
    MongoContext mongo,
    IObjectStorage storage,
    ITwitterPublisher twitter,
    ITelegramPublisher telegram,
    IFacebookPublisher facebook,
    IDiscordPostPublisher discord,
    FcmNotifier fcm,
    IClock clock,
    IOptions<IncidentPipelineOptions> options)
    : IEventHandler<PhotoApproved>
{
    private string Domain => options.Value.SocialLinkDomain;

    public async Task HandleAsync(PhotoApproved evt, CancellationToken ct)
    {
        var photo = await mongo.IncidentPhotos
            .Find(Builders<IncidentPhoto>.Filter.Eq(x => x.Id, evt.PhotoId))
            .FirstOrDefaultAsync(ct);
        if (photo is null || photo.Status != ModerationStatus.Approved || !photo.Public)
            return;

        var incident = await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, evt.IncidentId))
            .FirstOrDefaultAsync(ct);
        if (incident is null)
            return;

        var text = PhotoCopy.NewPhoto(incident, photo, Domain, clock);
        var image = await TryFetchImageAsync(photo.StorageKey, ct);

        await twitter.PublishAsync(new SocialPost { Text = text, ImageBytes = image }, ct: ct);
        await facebook.PublishAsync(new SocialPost { Text = text, ImageBytes = image }, ct: ct);
        await telegram.PublishAsync(new SocialPost { Text = text, ImageBytes = image }, ct: ct);
        await discord.PublishAsync(new SocialPost { Text = $"{text}\n{storage.PublicUrl(photo.StorageKey)}" }, ct: ct);

        await fcm.SendNotificationAsync(incident.Location, PhotoCopy.PushBody, fcm.Topics.Incident(incident.Id, includeImportant: false), ct: ct);
    }

    private async Task<byte[]?> TryFetchImageAsync(string storageKey, CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.GetAsync(storageKey, ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        catch
        {
            // Text-only post on fetch failure (publishers still deliver the copy + link).
            return null;
        }
    }
}
