using Fogos.Domain.Geo;

namespace Fogos.Domain.Alerts;

/// <summary>What a subscription watches: a concelho (by DICO) or a point + radius.</summary>
public enum AlertSubscriptionKind
{
    Concelho,
    Point,
}

/// <summary>
/// An anonymous alert subscription. A device registers a concelho or a point+radius watch; matched
/// incidents/risk land in <c>alert_events</c> (internal dedupe store). Purged after 90 days of no
/// <see cref="LastSeenAt"/> activity.
/// </summary>
public sealed class AlertSubscription
{
    /// <summary>Surrogate ObjectId (string).</summary>
    public string Id { get; set; } = "";

    public required AlertSubscriptionKind Kind { get; set; }

    /// <summary>Concelho DICO (Concelho subscriptions only).</summary>
    public string? Dico { get; set; }

    /// <summary>Watched point (Point subscriptions only).</summary>
    public GeoPoint? Point { get; set; }

    /// <summary>Match radius in km around <see cref="Point"/> (Point subscriptions only; ≤ 50).</summary>
    public double? RadiusKm { get; set; }

    /// <summary>Notify when the concelho risk reaches this level (4 or 5); null = no risk alerts.</summary>
    public int? RiskThreshold { get; set; }

    /// <summary>
    /// The local <c>users</c> id that owns this subscription (created while signed in). Null for anonymous
    /// device subscriptions. Owned subscriptions are exempt from the inactivity purge and only their owner
    /// (or nobody, when null) may mutate them.
    /// </summary>
    public string? OwnerUserId { get; set; }

    /// <summary>
    /// The <c>devices</c> id this subscription delivers to (Web Push), or null for poll-only / legacy
    /// subscriptions. Non-unique index; IgnoreIfNull (global convention) so existing documents are untouched.
    /// </summary>
    public string? DeviceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last time the device was seen active — drives the 90-day inactivity purge.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }
}
