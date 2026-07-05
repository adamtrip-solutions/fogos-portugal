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

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last time the device was seen active — drives the 90-day inactivity purge.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }
}
