namespace Fogos.Domain.Devices;

/// <summary>
/// The delivery platform of a <see cref="Device"/>. Only <see cref="Web"/> is used today; <see cref="Ios"/>
/// and <see cref="Android"/> are reserved for the Expo mobile plan (notifications-plan N1).
/// </summary>
public enum DevicePlatform
{
    Web,
    Ios,
    Android,
}

/// <summary>
/// A push-notification target registered by a browser (Web Push) or the mobile app (Expo). A web device
/// carries the Web Push subscription (endpoint + keys) the worker POSTs to when an alert matches; a mobile
/// app device instead carries a device-bound credential (<see cref="SecretHash"/>) that authenticates its
/// requests as the <c>App</c> tier. Alert subscriptions point at a device by its <see cref="Id"/>;
/// token/endpoint rotation is one re-registration and every subscription follows automatically
/// (notifications-plan N1 / mobile-app v1 device credentials).
/// </summary>
public sealed class Device
{
    /// <summary>
    /// A random 128-bit GUID ("N" format), generated app-side (web) or server-side (mobile) at registration.
    /// This is a capability-grade id: it is deliberately NOT a Mongo ObjectId so it cannot be enumerated —
    /// knowing it is proof the caller owns this device (it backs the deviceSubscriptions capability query and
    /// the mobile <c>X-Device-Key</c> credential).
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>Delivery platform. Web = browser Web Push; Ios/Android = the Expo mobile app.</summary>
    public required DevicePlatform Platform { get; set; }

    /// <summary>The Web Push service URL the worker POSTs the encrypted payload to (unique per subscription);
    /// null for mobile app devices, which have no Web Push endpoint.</summary>
    public string? PushEndpoint { get; set; }

    /// <summary>The subscription's P-256 ECDH public key (base64url), used to encrypt the payload.</summary>
    public string? PushP256dh { get; set; }

    /// <summary>The subscription's auth secret (base64url), used to encrypt the payload; null for app devices.</summary>
    public string? PushAuth { get; set; }

    /// <summary>
    /// SHA-256 hex hash of the mobile app device secret (never the plaintext — same posture as
    /// <c>ApiClient.KeyHash</c>). Set only for Ios/Android app devices; null for web push devices. The
    /// plaintext secret is returned exactly once by <c>registerAppDevice</c> and presented thereafter in the
    /// <c>X-Device-Key: fdv1.{deviceId}.{deviceSecret}</c> header.
    /// </summary>
    public string? SecretHash { get; set; }

    /// <summary>Reported device model (app devices only, e.g. <c>iPhone15,2</c>); null otherwise.</summary>
    public string? Model { get; set; }

    /// <summary>Reported app version (app devices only, e.g. <c>1.0.3</c>); null otherwise.</summary>
    public string? AppVersion { get; set; }

    /// <summary>
    /// When true the device credential is dead: <c>X-Device-Key</c> authentication 401s (honoured within the
    /// resolver's ≤60s cache TTL) and push/expo delivery skips it. Set by the admin CLI.
    /// </summary>
    public bool Revoked { get; set; }

    /// <summary>The local <c>users</c> id that owns this device when registered by a signed-in user; null = anonymous.</summary>
    public string? OwnerUserId { get; set; }

    /// <summary>Preferred locale reported by the browser (e.g. <c>pt-PT</c>); null when not supplied.</summary>
    public string? Locale { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last time the device re-registered — drives the inactivity purge (fallback to CreatedAt).</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Disabled after repeated delivery failures; disabled devices are skipped and eventually purged.</summary>
    public bool Disabled { get; set; }

    /// <summary>Consecutive delivery failures; auto-disables the device at the configured threshold.</summary>
    public int FailureCount { get; set; }
}
