using System.Xml.Linq;
using Fogos.Api.Auth;
using Fogos.Domain.Auth;
using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Time;
using Fogos.Domain.Warnings;
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

    /// <summary>
    /// Operator POSIT ("ponto de situação"): applies the reported means + situation narrative to an
    /// incident, then dispatches <see cref="IncidentResourcesChanged"/> so the existing worker handlers
    /// fire (incident_history snapshot + big-incident escalation) and subscribers see the change via the
    /// Mongo change stream. Ports <c>IncidentController::addPosit</c> — legacy set only extra/cos/pco; the
    /// clean schema also lets a POSIT report the committed means (which is what escalates). The separate
    /// "Novo Ponto de situação"/COS social posts are deliberately not revived here (they came from the
    /// disabled ANEPC-email path and are not part of the clean pipeline).
    /// </summary>
    [Authorize(Policy = ApiScopes.WriteIncidents)]
    public async Task<Incident> AddPosit(
        [ID] string incidentId,
        PositInput input,
        MongoContext mongo,
        IEventDispatcher dispatcher,
        IClock clock,
        CancellationToken ct)
    {
        var incident = await FindIncidentAsync(mongo, incidentId, ct)
                       ?? throw NotFound("Incident not found.", "INCIDENT_NOT_FOUND");

        var previous = incident.Resources;
        var current = previous with
        {
            Man = input.Man ?? previous.Man,
            Terrain = input.Terrain ?? previous.Terrain,
            Aerial = input.Aerial ?? previous.Aerial,
            Aquatic = input.Aquatic ?? previous.Aquatic,
            HeliFight = input.HeliFight ?? previous.HeliFight,
            HeliCoord = input.HeliCoord ?? previous.HeliCoord,
            PlaneFight = input.PlaneFight ?? previous.PlaneFight,
        };

        var update = Builders<Incident>.Update
            .Set(x => x.Resources, current)
            .Set(x => x.UpdatedAt, clock.UtcNow);

        // Legacy stored the POSIT narrative in `extra` (and cos/pco in dedicated fields the clean schema
        // dropped); fold the situation report into the incident's free-text extra when any part is given.
        var narrative = ComposePositNarrative(input);
        if (narrative is not null)
            update = update.Set(x => x.Extra, narrative);

        var updated = await mongo.Incidents.FindOneAndUpdateAsync(
            Builders<Incident>.Filter.Eq(x => x.Id, incident.Id),
            update,
            new FindOneAndUpdateOptions<Incident> { ReturnDocument = ReturnDocument.After },
            ct) ?? incident;

        await dispatcher.DispatchAsync(new IncidentResourcesChanged(updated.Id, previous, current), ct: ct);
        return updated;
    }

    /// <summary>
    /// Attaches a KML perimeter to an incident (VOST-curated slot when <paramref name="vost"/> is true,
    /// else the ANEPC slot), then dispatches <see cref="KmlAttached"/> so the worker announces the new
    /// "área de interesse" (renderer screenshot + threaded tweet, dry-run, text-only on renderer failure).
    /// Ports <c>IncidentController::addKML</c>. The payload must parse as XML with a <c>&lt;kml&gt;</c> root.
    /// </summary>
    [Authorize(Policy = ApiScopes.WriteIncidents)]
    public async Task<Incident> AttachKml(
        [ID] string incidentId,
        string kml,
        MongoContext mongo,
        IEventDispatcher dispatcher,
        IClock clock,
        CancellationToken ct,
        bool? vost = null)
    {
        if (!IsWellFormedKml(kml))
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("Payload is not a valid KML document (expected XML with a <kml> root).")
                .SetCode("KML_INVALID").Build());

        var incident = await FindIncidentAsync(mongo, incidentId, ct)
                       ?? throw NotFound("Incident not found.", "INCIDENT_NOT_FOUND");

        var isVost = vost ?? false;
        var slot = isVost
            ? Builders<Incident>.Update.Set(x => x.KmlVost, kml)
            : Builders<Incident>.Update.Set(x => x.Kml, kml);
        var update = Builders<Incident>.Update.Combine(slot, Builders<Incident>.Update.Set(x => x.UpdatedAt, clock.UtcNow));

        var updated = await mongo.Incidents.FindOneAndUpdateAsync(
            Builders<Incident>.Filter.Eq(x => x.Id, incident.Id),
            update,
            new FindOneAndUpdateOptions<Incident> { ReturnDocument = ReturnDocument.After },
            ct) ?? incident;

        await dispatcher.DispatchAsync(new KmlAttached(updated.Id, isVost), ct: ct);
        return updated;
    }

    /// <summary>
    /// Issues a broadcast warning (MANUAL or AGIF). Persists to <c>warnings</c> with the caller as issuer;
    /// the Mongo change stream fans it out to <c>warningAdded</c> subscribers (so this never publishes that
    /// topic itself — exactly-once), while <see cref="WarningCreated"/> drives the queue/social side.
    /// Ports <c>WarningsController::add</c> / <c>::addAgif</c>. SITE warnings are banner data, not creatable here.
    /// </summary>
    [Authorize(Policy = ApiScopes.WriteWarnings)]
    public async Task<Warning> AddWarning(
        WarningKind kind,
        WarningInput input,
        MongoContext mongo,
        IEventDispatcher dispatcher,
        IClock clock,
        IFogosCallerAccessor callerAccessor,
        CancellationToken ct)
    {
        if (kind == WarningKind.Site)
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("SITE warnings are site-banner data and cannot be created via addWarning.")
                .SetCode("WARNING_KIND_UNSUPPORTED").Build());

        var warning = new Warning
        {
            Kind = kind,
            Message = input.Message,
            Url = string.IsNullOrWhiteSpace(input.Url) ? null : input.Url,
            IssuedBy = callerAccessor.Caller.Name,
            CreatedAt = clock.UtcNow,
        };

        await mongo.Warnings.InsertOneAsync(warning, cancellationToken: ct);
        await dispatcher.DispatchAsync(new WarningCreated(warning.Id, kind), ct: ct);
        return warning;
    }

    private static string? ComposePositNarrative(PositInput input)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(input.Notes)) parts.Add(input.Notes.Trim());
        if (!string.IsNullOrWhiteSpace(input.Cos)) parts.Add($"COS: {input.Cos.Trim()}");
        if (!string.IsNullOrWhiteSpace(input.Pco)) parts.Add($"PCO: {input.Pco.Trim()}");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static bool IsWellFormedKml(string kml)
    {
        if (string.IsNullOrWhiteSpace(kml))
            return false;
        try
        {
            var root = XDocument.Parse(kml).Root;
            return root is not null && string.Equals(root.Name.LocalName, "kml", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static GraphQLException NotFound(string message, string code) =>
        new(ErrorBuilder.New().SetMessage(message).SetCode(code).Build());

    private static async Task<Incident?> FindIncidentAsync(MongoContext mongo, string incidentId, CancellationToken ct) =>
        await mongo.Incidents
            .Find(Builders<Incident>.Filter.Eq(x => x.Id, incidentId))
            .FirstOrDefaultAsync(ct);

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
