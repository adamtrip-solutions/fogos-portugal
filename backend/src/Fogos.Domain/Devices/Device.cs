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
/// A push-notification target registered by a browser (or, later, a mobile app). One device carries the
/// Web Push subscription (endpoint + keys) the worker POSTs to when an alert matches. Alert subscriptions
/// point at a device by its <see cref="Id"/>; token/endpoint rotation is one re-registration and every
/// subscription follows automatically (notifications-plan N1).
/// </summary>
public sealed class Device
{
    /// <summary>
    /// A random 128-bit GUID ("N" format), generated app-side at registration. This is a capability-grade
    /// id: it is deliberately NOT a Mongo ObjectId so it cannot be enumerated — knowing it is proof the
    /// caller owns this device (it backs the deviceSubscriptions capability query).
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>Delivery platform. Web today; Ios/Android reserved for the mobile plan.</summary>
    public required DevicePlatform Platform { get; set; }

    /// <summary>The Web Push service URL the worker POSTs the encrypted payload to (unique per subscription).</summary>
    public required string PushEndpoint { get; set; }

    /// <summary>The subscription's P-256 ECDH public key (base64url), used to encrypt the payload.</summary>
    public string? PushP256dh { get; set; }

    /// <summary>The subscription's auth secret (base64url), used to encrypt the payload.</summary>
    public required string PushAuth { get; set; }

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
