using System.Xml.Linq;
using Fogos.Api.Auth;
using Fogos.Api.GraphQL.Types;
using Fogos.Domain.Alerts;
using Fogos.Domain.Auth;
using Fogos.Domain.Events;
using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Time;
using Fogos.Domain.Warnings;
using Fogos.Domain.Webhooks;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.RateLimiting;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Storage;
using Fogos.Infrastructure.Webhooks;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using Microsoft.Extensions.Options;
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
        Fogos.Infrastructure.Incidents.KmlVersionStore kmlVersions,
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

        // Version the perimeter (dedup by SHA-256 per incident+slot) before announcing.
        await kmlVersions.AppendIfChangedAsync(updated.Id, isVost, kml, ct);

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

    /// <summary>
    /// Registers an anonymous alert subscription (Concelho by DICO, or Point + radius). Rate-limited per
    /// caller IP like the photo-upload gate. Validates: Concelho ⇒ DICO exists in <c>locations</c>;
    /// Point ⇒ radius 1–50 km and the point inside the Portugal bounding box; risk threshold ∈ {4,5}.
    /// </summary>
    public async Task<AlertSubscription> CreateAlertSubscription(
        CreateAlertSubscriptionInput input,
        MongoContext mongo,
        LocationReads locations,
        AlertSubscriptionGate gate,
        IClock clock,
        IFogosCallerAccessor callerAccessor,
        IOptions<AlertOptions> options,
        CancellationToken ct)
    {
        if (!await gate.TryAcquireAsync(callerAccessor.Caller.RemoteIp, ct))
            throw Fail("Demasiados pedidos de subscrição. Tente novamente dentro de momentos.", "RATE_LIMITED");

        var o = options.Value;
        var subscription = new AlertSubscription
        {
            Kind = input.Kind,
            FcmToken = string.IsNullOrWhiteSpace(input.FcmToken) ? null : input.FcmToken,
            CreatedAt = clock.UtcNow,
        };

        if (input.Kind == AlertSubscriptionKind.Concelho)
        {
            if (string.IsNullOrWhiteSpace(input.Dico))
                throw Fail("O concelho (dico) é obrigatório para subscrições de concelho.", "ALERT_DICO_REQUIRED");
            if (await locations.ByDicoAsync(input.Dico, ct) is null)
                throw Fail("Concelho desconhecido.", "ALERT_DICO_UNKNOWN");
            subscription.Dico = input.Dico;

            if (input.RiskThreshold is int rt && rt is not (4 or 5))
                throw Fail("O limiar de risco tem de ser 4 ou 5.", "ALERT_RISK_THRESHOLD");
            subscription.RiskThreshold = input.RiskThreshold;
        }
        else // Point
        {
            // Risk is a per-concelho signal; it has no meaning for a point subscription.
            if (input.RiskThreshold is not null)
                throw Fail("O limiar de risco só se aplica a subscrições por concelho.", "ALERT_RISK_THRESHOLD_SCOPE");
            if (input.Latitude is not double lat || input.Longitude is not double lng)
                throw Fail("A latitude e a longitude são obrigatórias para subscrições por ponto.", "ALERT_POINT_REQUIRED");
            if (input.RadiusKm is not double radius)
                throw Fail("O raio é obrigatório para subscrições por ponto.", "ALERT_RADIUS_REQUIRED");
            if (radius < 1 || radius > o.MaxRadiusKm)
                throw Fail($"O raio tem de estar entre 1 e {o.MaxRadiusKm:0} km.", "ALERT_RADIUS_RANGE");
            if (!o.InPortugal(lat, lng))
                throw Fail("O ponto está fora de Portugal.", "ALERT_POINT_OUT_OF_BOUNDS");

            subscription.Point = GeoPoint.FromLatLng(lat, lng);
            subscription.RadiusKm = radius;
        }

        await mongo.AlertSubscriptions.InsertOneAsync(subscription, cancellationToken: ct);
        return subscription;
    }

    /// <summary>Deletes an alert subscription by id. Returns false when the id is unknown/malformed.</summary>
    public async Task<bool> DeleteAlertSubscription([ID] string id, MongoContext mongo, CancellationToken ct)
    {
        try
        {
            var result = await mongo.AlertSubscriptions.DeleteOneAsync(
                Builders<AlertSubscription>.Filter.Eq(x => x.Id, id), ct);
            return result.DeletedCount > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Registers an outbound webhook for the authenticated API client. HTTPS-only URL, valid event names,
    /// max 3 per client. The response carries the signing secret once — it is never exposed again.
    /// </summary>
    public async Task<Webhook> RegisterWebhook(
        string url,
        IReadOnlyList<string> events,
        MongoContext mongo,
        WebhookReads webhooks,
        IClock clock,
        IFogosCallerAccessor callerAccessor,
        IOptions<WebhookOptions> options,
        CancellationToken ct)
    {
        var clientId = RequireClient(callerAccessor);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw Fail("O URL do webhook tem de usar HTTPS.", "WEBHOOK_URL_INVALID");

        var chosen = events.Distinct().ToList();
        if (chosen.Count == 0 || chosen.Any(e => !WebhookEvents.All.Contains(e)))
            throw Fail("Lista de eventos inválida.", "WEBHOOK_EVENTS_INVALID");

        if (await webhooks.CountByClientAsync(clientId, ct) >= options.Value.MaxEndpointsPerClient)
            throw Fail($"Máximo de {options.Value.MaxEndpointsPerClient} webhooks por cliente atingido.", "WEBHOOK_LIMIT");

        var endpoint = new WebhookEndpoint
        {
            ClientId = clientId,
            Url = url,
            Secret = WebhookSigner.NewSecret(),
            Events = chosen,
            Active = true,
            CreatedAt = clock.UtcNow,
        };
        await mongo.WebhookEndpoints.InsertOneAsync(endpoint, cancellationToken: ct);

        return Webhook.WithSecret(endpoint);
    }

    /// <summary>Deletes one of the caller's own webhooks. Returns false when unknown or not owned.</summary>
    public async Task<bool> DeleteWebhook(
        [ID] string id, MongoContext mongo, IFogosCallerAccessor callerAccessor, CancellationToken ct)
    {
        var clientId = RequireClient(callerAccessor);
        try
        {
            var result = await mongo.WebhookEndpoints.DeleteOneAsync(
                Builders<WebhookEndpoint>.Filter.Eq(x => x.Id, id) & Builders<WebhookEndpoint>.Filter.Eq(x => x.ClientId, clientId), ct);
            return result.DeletedCount > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Resolves the authenticated client id, or throws when the caller is anonymous.</summary>
    private static string RequireClient(IFogosCallerAccessor accessor)
    {
        var caller = accessor.Caller;
        if (caller.IsAnonymous || string.IsNullOrEmpty(caller.ClientId))
            throw Fail("É necessária autenticação de cliente.", "UNAUTHENTICATED");
        return caller.ClientId;
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

    private static GraphQLException Fail(string message, string code) =>
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
