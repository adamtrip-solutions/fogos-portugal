using System.Xml.Linq;
using Fogos.Api.Auth;
using Fogos.Api.GraphQL.Types;
using Fogos.Domain.Alerts;
using Fogos.Domain.Auth;
using Fogos.Domain.Devices;
using Fogos.Domain.Events;
using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Time;
using Fogos.Domain.Webhooks;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
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
    /// else the ANEPC slot), then dispatches <see cref="KmlAttached"/>. Ports
    /// <c>IncidentController::addKML</c>. The payload must parse as XML with a <c>&lt;kml&gt;</c> root.
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
    /// Registers an alert subscription (Concelho by DICO, or Point + radius). Rate-limited per caller IP
    /// like the photo-upload gate. Validates: Concelho ⇒ DICO exists in <c>locations</c>; Point ⇒ radius
    /// 1–50 km and the point inside the Portugal bounding box; risk threshold ∈ {4,5}. When the caller is a
    /// signed-in user the subscription is owned (per-user cap enforced, exempt from the inactivity purge);
    /// anonymous callers create unowned subscriptions exactly as before.
    /// </summary>
    public async Task<AlertSubscription> CreateAlertSubscription(
        CreateAlertSubscriptionInput input,
        MongoContext mongo,
        LocationReads locations,
        AlertSubscriptionGate gate,
        AccountReads accounts,
        DeviceReads devices,
        IClock clock,
        IFogosCallerAccessor callerAccessor,
        IOptions<AlertOptions> options,
        CancellationToken ct)
    {
        var caller = callerAccessor.Caller;
        if (!await gate.TryAcquireAsync(caller.RemoteIp, ct))
            throw Fail("Demasiados pedidos de subscrição. Tente novamente dentro de momentos.", "RATE_LIMITED");

        var o = options.Value;
        if (caller.IsUser && caller.UserId is { } userId
            && await accounts.CountAlertSubscriptionsByUserAsync(userId, ct) >= o.MaxSubscriptionsPerUser)
            throw Fail($"Máximo de {o.MaxSubscriptionsPerUser} subscrições por utilizador atingido.", "ALERT_SUBSCRIPTION_LIMIT");

        var subscription = new AlertSubscription
        {
            Kind = input.Kind,
            OwnerUserId = caller.UserId,
            // A device caller (App tier) owns the subscription by its device id — the device-analogue of
            // OwnerUserId. Only that device may later update/delete it.
            DeviceId = caller.DeviceId,
            CreatedAt = clock.UtcNow,
        };
        await ValidateAndApplyAsync(subscription, input, locations, o, ct);

        // Optional Web Push binding (anonymous / signed-in web callers only): the device must exist and be
        // enabled (capability check by its GUID id). An owned device may only be bound by its owner; a
        // mismatch reports exactly like not-found so the response never becomes an existence oracle for
        // another user's device id. Skipped for App-tier device callers, whose subscription is already bound
        // to their own device above.
        if (caller.DeviceId is null && !string.IsNullOrEmpty(input.DeviceId))
        {
            var device = await devices.GetByIdAsync(input.DeviceId, ct);
            if (device is null || device.Disabled
                || (device.OwnerUserId is not null && device.OwnerUserId != caller.UserId))
                throw Fail("Dispositivo desconhecido.", "DEVICE_NOT_FOUND");
            subscription.DeviceId = device.Id;
        }

        await mongo.AlertSubscriptions.InsertOneAsync(subscription, cancellationToken: ct);
        return subscription;
    }

    /// <summary>
    /// Registers a mobile app install on first launch: creates an app <c>devices</c> document and mints its
    /// device-bound credential. The secret is generated with the shared key generator, returned ONCE, and
    /// stored only as its SHA-256 hash (never plaintext — the <c>ApiClient</c> posture). Anonymous-allowed and
    /// per-IP rate-limited with the same <see cref="DeviceRegistrationGate"/> as Web Push registration. The
    /// client presents the credential thereafter as <c>X-Device-Key: fdv1.{deviceId}.{deviceSecret}</c>.
    /// </summary>
    public async Task<AppDeviceCredential> RegisterAppDevice(
        RegisterAppDeviceInput input,
        MongoContext mongo,
        DeviceRegistrationGate gate,
        IClock clock,
        IFogosCallerAccessor callerAccessor,
        CancellationToken ct)
    {
        var caller = callerAccessor.Caller;
        if (!await gate.TryAcquireAsync(caller.RemoteIp, ct))
            throw Fail("Demasiados registos de dispositivo. Tente novamente dentro de momentos.", "RATE_LIMITED");

        var secret = ApiKeyGenerator.NewPlaintext();
        var now = clock.UtcNow;
        var device = new Device
        {
            Id = Guid.NewGuid().ToString("N"), // capability id — random, NOT an enumerable ObjectId.
            Platform = input.Platform == AppPlatform.Ios ? DevicePlatform.Ios : DevicePlatform.Android,
            SecretHash = ApiKeyGenerator.Hash(secret),
            Model = Clean(input.Model),
            AppVersion = Clean(input.AppVersion),
            CreatedAt = now,
            LastSeenAt = now,
        };
        await mongo.Devices.InsertOneAsync(device, cancellationToken: ct);

        return new AppDeviceCredential(device.Id, secret);
    }

    /// <summary>
    /// Registers (or refreshes) a browser's Web Push subscription as a <c>devices</c> document, returning the
    /// device's capability id (a random GUID the browser persists). Anonymous-allowed and per-IP rate-limited
    /// exactly like <see cref="CreateAlertSubscription"/>. The endpoint must be an absolute https URL on an
    /// allow-listed push host (SSRF guard) and the keys valid P-256/auth material. Upsert by endpoint is
    /// key-gated: a browser re-registering always presents the same p256dh for the same endpoint (keys never
    /// rotate without a new endpoint), so a matching key refreshes LastSeenAt / re-enables the device and
    /// adopts the signed-in owner when it was anonymous — while a differing key is rejected with a generic
    /// error, updating nothing and never leaking the id (an endpoint alone must not escalate into the device
    /// capability or let an attacker overwrite keys to silence delivery). Errors <c>WEB_PUSH_DISABLED</c>
    /// when no VAPID key is configured.
    /// </summary>
    public async Task<RegisteredDevice> RegisterWebPushDevice(
        RegisterWebPushDeviceInput input,
        MongoContext mongo,
        DeviceRegistrationGate gate,
        IClock clock,
        IFogosCallerAccessor callerAccessor,
        IOptions<WebPushOptions> options,
        CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured)
            throw Fail("As notificações Web Push não estão configuradas.", "WEB_PUSH_DISABLED");

        var caller = callerAccessor.Caller;
        if (!await gate.TryAcquireAsync(caller.RemoteIp, ct))
            throw Fail("Demasiados registos de dispositivo. Tente novamente dentro de momentos.", "RATE_LIMITED");

        var validation = WebPushRegistration.Validate(
            new WebPushSubscriptionInput(input.Endpoint, input.P256dh, input.Auth), o.AllowedEndpointHosts);
        if (validation != WebPushValidationError.None)
            throw validation switch
            {
                WebPushValidationError.EndpointHostNotAllowed =>
                    Fail("O serviço de push não é permitido.", "WEB_PUSH_ENDPOINT_NOT_ALLOWED"),
                WebPushValidationError.KeyInvalid =>
                    Fail("As chaves de push são inválidas.", "WEB_PUSH_KEYS_INVALID"),
                _ => Fail("O endpoint de push é inválido.", "WEB_PUSH_ENDPOINT_INVALID"),
            };

        var now = clock.UtcNow;
        var existing = await mongo.Devices
            .Find(Builders<Device>.Filter.Eq(x => x.PushEndpoint, input.Endpoint))
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
            return await RefreshRegistrationAsync(mongo, existing, input, caller, now, ct);

        var device = new Device
        {
            Id = Guid.NewGuid().ToString("N"), // capability id — random, NOT an enumerable ObjectId.
            Platform = DevicePlatform.Web,
            PushEndpoint = input.Endpoint,
            PushP256dh = input.P256dh,
            PushAuth = input.Auth,
            OwnerUserId = caller.UserId,
            CreatedAt = now,
            LastSeenAt = now,
        };
        try
        {
            await mongo.Devices.InsertOneAsync(device, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Lost a registration race on the unique pushEndpoint index — converge on the winner through
            // the same key-gated refresh instead of surfacing a raw duplicate-key error.
            existing = await mongo.Devices
                .Find(Builders<Device>.Filter.Eq(x => x.PushEndpoint, input.Endpoint))
                .FirstOrDefaultAsync(ct);
            if (existing is null)
                throw Fail("A subscrição de push é inválida.", "INVALID_PUSH_SUBSCRIPTION");
            return await RefreshRegistrationAsync(mongo, existing, input, caller, now, ct);
        }
        return new RegisteredDevice(device.Id);
    }

    /// <summary>
    /// Key-gated re-registration of an existing device (same endpoint). A legitimate browser always presents
    /// the SAME p256dh for the same endpoint — keys never rotate without a new endpoint — so a differing key
    /// means an endpoint-only caller fishing for the device id or trying to overwrite the keys (a silent
    /// delivery DoS): reject with a generic error, update nothing, leak nothing. A matching key bumps
    /// LastSeenAt, re-enables the device, and adopts the signed-in owner when it was anonymous.
    /// </summary>
    private static async Task<RegisteredDevice> RefreshRegistrationAsync(
        MongoContext mongo, Device existing, RegisterWebPushDeviceInput input, FogosCaller caller,
        DateTimeOffset now, CancellationToken ct)
    {
        if (!string.Equals(existing.PushP256dh, input.P256dh, StringComparison.Ordinal))
            throw Fail("A subscrição de push é inválida.", "INVALID_PUSH_SUBSCRIPTION");

        var update = Builders<Device>.Update
            .Set(x => x.LastSeenAt, now)
            .Set(x => x.Disabled, false)
            .Set(x => x.FailureCount, 0);
        // Adopt the signed-in owner if the device was registered anonymously.
        if (caller.IsUser && caller.UserId is { } uid && existing.OwnerUserId is null)
            update = update.Set(x => x.OwnerUserId, uid);

        await mongo.Devices.UpdateOneAsync(Builders<Device>.Filter.Eq(x => x.Id, existing.Id), update, cancellationToken: ct);
        return new RegisteredDevice(existing.Id);
    }

    /// <summary>
    /// Unsubscribes a browser: deletes the device whose push endpoint is <paramref name="endpoint"/> (the
    /// endpoint IS the capability — only the owning browser knows it) and cascades — dropping its anonymous
    /// alert subscriptions and clearing <c>DeviceId</c> on owned ones. Returns false when the endpoint is unknown.
    /// </summary>
    public async Task<bool> DeleteWebPushDevice(
        string endpoint, MongoContext mongo, DeviceStore deviceStore, CancellationToken ct)
    {
        var device = await mongo.Devices
            .Find(Builders<Device>.Filter.Eq(x => x.PushEndpoint, endpoint))
            .FirstOrDefaultAsync(ct);
        if (device is null)
            return false;

        await deviceStore.DeleteWithCascadeAsync(device.Id, ct);
        return true;
    }

    /// <summary>
    /// Updates one of the caller's own alert subscriptions, re-running the full create validation. Owner-only:
    /// a signed-in user may touch only their own (OwnerUserId) subscriptions; an App-tier device may touch
    /// only its own (DeviceId) subscriptions. Throws when the id is unknown or owned by someone else.
    /// </summary>
    public async Task<AlertSubscription> UpdateAlertSubscription(
        [ID] string id,
        CreateAlertSubscriptionInput input,
        MongoContext mongo,
        LocationReads locations,
        IFogosCallerAccessor callerAccessor,
        IOptions<AlertOptions> options,
        CancellationToken ct)
    {
        // Update is owner-only: a signed-in user or an App-tier device. Anonymous callers (who cannot prove
        // ownership) recreate rather than update, exactly as before this mutation learned about devices.
        var caller = callerAccessor.Caller;
        if (caller.UserId is null && caller.DeviceId is null)
            throw Fail("É necessária autenticação de utilizador ou dispositivo.", "UNAUTHENTICATED");

        AlertSubscription? existing;
        try
        {
            var f = Builders<AlertSubscription>.Filter;
            existing = await mongo.AlertSubscriptions
                .Find(f.Eq(x => x.Id, id) & OwnershipFilter(caller))
                .FirstOrDefaultAsync(ct);
        }
        catch (FormatException)
        {
            existing = null;
        }

        if (existing is null)
            throw Fail("Subscrição não encontrada.", "ALERT_SUBSCRIPTION_NOT_FOUND");

        // Re-validate against the new input, then reset the fields of the other kind so the document stays
        // consistent when the subscription switches between Concelho and Point.
        existing.Dico = null;
        existing.RiskThreshold = null;
        existing.Point = null;
        existing.RadiusKm = null;
        await ValidateAndApplyAsync(existing, input, locations, options.Value, ct);

        // Replace re-serialises through the class map (IgnoreIfNull), so cleared fields are unset — while
        // Id, OwnerUserId, CreatedAt, and LastSeenAt are preserved from the loaded document.
        await mongo.AlertSubscriptions.ReplaceOneAsync(
            Builders<AlertSubscription>.Filter.Eq(x => x.Id, existing.Id), existing, cancellationToken: ct);
        return existing;
    }

    /// <summary>
    /// Deletes an alert subscription by id. An App-tier device may delete only its own (DeviceId)
    /// subscriptions — never another device's. A user/anonymous caller may delete unowned subscriptions or
    /// those they own — so an anonymous caller can no longer delete a user-owned subscription. Returns false
    /// when the id is unknown, malformed, or not owned by the caller.
    /// </summary>
    public async Task<bool> DeleteAlertSubscription(
        [ID] string id, MongoContext mongo, IFogosCallerAccessor callerAccessor, CancellationToken ct)
    {
        var f = Builders<AlertSubscription>.Filter;
        try
        {
            var result = await mongo.AlertSubscriptions.DeleteOneAsync(
                f.Eq(x => x.Id, id) & OwnershipFilter(callerAccessor.Caller), ct);
            return result.DeletedCount > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// The mutate-ownership predicate for alert subscriptions. An App-tier device caller may touch only
    /// subscriptions bound to its own device id. A user/anonymous caller may touch unowned subscriptions or
    /// those they own (a signed-in user's OwnerUserId). This is what keeps one device out of another device's
    /// subscriptions while leaving anonymous/IP-created subscriptions freely mutable as before.
    /// </summary>
    private static FilterDefinition<AlertSubscription> OwnershipFilter(FogosCaller caller)
    {
        var f = Builders<AlertSubscription>.Filter;
        if (caller.DeviceId is { } deviceId)
            return f.Eq(x => x.DeviceId, deviceId);
        return f.Or(f.Eq(x => x.OwnerUserId, null), f.Eq(x => x.OwnerUserId, caller.UserId));
    }

    /// <summary>
    /// Validates <paramref name="input"/> and applies the Concelho/Point fields onto <paramref name="sub"/>.
    /// Shared by create and update so both enforce the exact same rules.
    /// </summary>
    private static async Task ValidateAndApplyAsync(
        AlertSubscription sub, CreateAlertSubscriptionInput input, LocationReads locations, AlertOptions o, CancellationToken ct)
    {
        sub.Kind = input.Kind;

        if (input.Kind == AlertSubscriptionKind.Concelho)
        {
            if (string.IsNullOrWhiteSpace(input.Dico))
                throw Fail("O concelho (dico) é obrigatório para subscrições de concelho.", "ALERT_DICO_REQUIRED");
            if (await locations.ByDicoAsync(input.Dico, ct) is null)
                throw Fail("Concelho desconhecido.", "ALERT_DICO_UNKNOWN");
            sub.Dico = input.Dico;

            if (input.RiskThreshold is int rt && rt is not (4 or 5))
                throw Fail("O limiar de risco tem de ser 4 ou 5.", "ALERT_RISK_THRESHOLD");
            sub.RiskThreshold = input.RiskThreshold;
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

            sub.Point = GeoPoint.FromLatLng(lat, lng);
            sub.RadiusKm = radius;
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

    /// <summary>
    /// Mints a self-service API key for the signed-in user (Registered tier, empty scopes). The plaintext is
    /// returned once and never stored; only its SHA-256 hash and a display prefix are persisted. Enforces the
    /// per-user cap over the user's active keys.
    /// </summary>
    public async Task<CreatedApiKey> CreateApiKey(
        string name,
        MongoContext mongo,
        AccountReads accounts,
        IClock clock,
        IFogosCallerAccessor callerAccessor,
        IOptions<AuthOptions> options,
        CancellationToken ct)
    {
        var userId = RequireUser(callerAccessor);

        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw Fail("O nome da chave é obrigatório.", "API_KEY_NAME_REQUIRED");

        if (await accounts.CountActiveApiKeysByUserAsync(userId, ct) >= options.Value.MaxApiKeysPerUser)
            throw Fail($"Máximo de {options.Value.MaxApiKeysPerUser} chaves de API por utilizador atingido.", "API_KEY_LIMIT");

        var plaintext = ApiKeyGenerator.NewPlaintext();
        var client = new ApiClient
        {
            Name = trimmed,
            KeyHash = ApiKeyGenerator.Hash(plaintext),
            Tier = ApiTier.Registered,
            Scopes = [],
            OwnerUserId = userId,
            KeyPrefix = plaintext[..ApiKeyGenerator.PrefixLength],
            CreatedAt = clock.UtcNow,
        };
        await mongo.ApiClients.InsertOneAsync(client, cancellationToken: ct);

        return new CreatedApiKey(ApiKeyInfo.From(client), plaintext);
    }

    /// <summary>
    /// Revokes one of the caller's own API keys. Returns false when the key is unknown, malformed, already
    /// revoked, or owned by another user. The change may take up to ~60s to take effect on the API's
    /// in-memory key-resolution cache.
    /// </summary>
    public async Task<bool> RevokeApiKey(
        [ID] string id, MongoContext mongo, IClock clock, IFogosCallerAccessor callerAccessor, CancellationToken ct)
    {
        var userId = RequireUser(callerAccessor);
        var f = Builders<ApiClient>.Filter;
        try
        {
            var result = await mongo.ApiClients.UpdateOneAsync(
                f.Eq(x => x.Id, id) & f.Eq(x => x.OwnerUserId, userId) & f.Eq(x => x.RevokedAt, null),
                Builders<ApiClient>.Update.Set(x => x.RevokedAt, clock.UtcNow), cancellationToken: ct);
            return result.ModifiedCount > 0;
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

    /// <summary>Resolves the signed-in user id, or throws when the caller is not a signed-in human.</summary>
    private static string RequireUser(IFogosCallerAccessor accessor)
    {
        var caller = accessor.Caller;
        if (!caller.IsUser || string.IsNullOrEmpty(caller.UserId))
            throw Fail("É necessária autenticação de utilizador.", "UNAUTHENTICATED");
        return caller.UserId;
    }

    /// <summary>Trims optional free-text metadata to null when blank (so IgnoreIfNull keeps it out of Mongo).</summary>
    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
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
