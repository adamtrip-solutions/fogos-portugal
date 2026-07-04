using Fogos.Api.Auth;
using Fogos.Domain.Auth;
using Fogos.Domain.Events;
using Fogos.Domain.Photos;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Storage;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using MongoDB.Driver;

namespace Fogos.Api.GraphQL.Mutations;

/// <summary>Moderator decision on a pending photo.</summary>
public enum ModerationDecision
{
    Approve,
    Reject,
}

/// <summary>
/// The write schema root (first mutation type in the schema). Photo moderation, gated by the
/// <c>moderate:photos</c> scope policy. Ports <c>PhotoModerationController</c>: APPROVE publishes (and
/// optionally fans out to socials/push via the <see cref="PhotoApproved"/> event); REJECT deletes the
/// stored object and the document. The queue itself is read via <c>Query.pendingPhotos</c>.
/// </summary>
public sealed class Mutation
{
    [Authorize(Policy = ApiScopes.ModeratePhotos)]
    public async Task<IncidentPhoto> ModeratePhoto(
        [ID] string photoId,
        ModerationDecision decision,
        MongoContext mongo,
        IObjectStorage storage,
        IEventDispatcher dispatcher,
        IClock clock,
        IFogosCallerAccessor callerAccessor,
        CancellationToken ct,
        bool? publish = null)
    {
        var photo = await FindAsync(mongo, photoId, ct)
                    ?? throw new GraphQLException(ErrorBuilder.New().SetMessage("Photo not found.").SetCode("PHOTO_NOT_FOUND").Build());

        var moderator = callerAccessor.Caller.Name;
        var now = clock.UtcNow;

        if (decision == ModerationDecision.Reject)
        {
            // Legacy behaviour: drop the object then the document. Return the (final) rejected view.
            try
            {
                await storage.DeleteAsync(photo.StorageKey, ct);
            }
            catch
            {
                // Object cleanup is best-effort; the document delete must still happen.
            }

            await mongo.IncidentPhotos.DeleteOneAsync(Builders<IncidentPhoto>.Filter.Eq(x => x.Id, photo.Id), ct);

            photo.Status = ModerationStatus.Rejected;
            photo.Moderation = new PhotoModeration(now, "reject", moderator);
            photo.UpdatedAt = now;
            return photo;
        }

        // Approve: publish defaults to true; approved photos may still be held back from public listings.
        var isPublic = publish ?? true;
        var update = Builders<IncidentPhoto>.Update
            .Set(x => x.Status, ModerationStatus.Approved)
            .Set(x => x.Public, isPublic)
            .Set(x => x.Moderation, new PhotoModeration(now, "approve", moderator))
            .Set(x => x.UpdatedAt, now);

        var updated = await mongo.IncidentPhotos.FindOneAndUpdateAsync(
            Builders<IncidentPhoto>.Filter.Eq(x => x.Id, photo.Id),
            update,
            new FindOneAndUpdateOptions<IncidentPhoto> { ReturnDocument = ReturnDocument.After },
            ct) ?? photo;

        // Social/push fan-out mirrors PhotoModerationController::approve — only when the photo goes public.
        if (isPublic)
            await dispatcher.DispatchAsync(new PhotoApproved(updated.Id, updated.IncidentId), ct: ct);

        return updated;
    }

    private static async Task<IncidentPhoto?> FindAsync(MongoContext mongo, string photoId, CancellationToken ct)
    {
        try
        {
            return await mongo.IncidentPhotos
                .Find(Builders<IncidentPhoto>.Filter.Eq(x => x.Id, photoId))
                .FirstOrDefaultAsync(ct);
        }
        catch (FormatException)
        {
            return null; // photoId is not a valid object id → treated as not found.
        }
    }
}
