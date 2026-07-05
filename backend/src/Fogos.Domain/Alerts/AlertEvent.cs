namespace Fogos.Domain.Alerts;

/// <summary>The kinds of alert an <see cref="AlertEvent"/> can carry.</summary>
public static class AlertEventKind
{
    public const string NewIncident = "NEW_INCIDENT";
    public const string Escalation = "ESCALATION";
    public const string Rekindle = "REKINDLE";
    public const string Risk = "RISK";
}

/// <summary>
/// A delivered alert for one subscription (poll-first). De-duplicated per subscription by
/// <see cref="DedupeKey"/> (unique index): <c>inc:{id}</c> / <c>esc:{id}</c> / <c>rek:{id}</c> /
/// <c>risk:{dico}:{yyyy-MM-dd}</c>. TTL-expired after 7 days.
/// </summary>
public sealed class AlertEvent
{
    /// <summary>Surrogate ObjectId (string).</summary>
    public string Id { get; set; } = "";

    /// <summary>The subscription this event was matched for (its string _id).</summary>
    public required string SubscriptionId { get; set; }

    /// <summary>One of <see cref="AlertEventKind"/>.</summary>
    public required string Kind { get; set; }

    /// <summary>The related incident, when the alert is incident-driven.</summary>
    public string? IncidentId { get; set; }

    /// <summary>European-Portuguese message shown to the user.</summary>
    public required string Message { get; set; }

    /// <summary>Per-subscription dedupe key (unique with <see cref="SubscriptionId"/>).</summary>
    public required string DedupeKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
