namespace Fogos.Domain.Webhooks;

/// <summary>The event names an API client may subscribe a webhook to.</summary>
public static class WebhookEvents
{
    public const string IncidentCreated = "incident.created";
    public const string IncidentEscalating = "incident.escalating";
    public const string IncidentRekindle = "incident.rekindle";
    public const string WarningCreated = "warning.created";
    public const string ReportCreated = "report.created";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        IncidentCreated, IncidentEscalating, IncidentRekindle, WarningCreated, ReportCreated,
    };
}

/// <summary>
/// A registered outbound webhook for an API client. Deliveries are signed HMAC-SHA256 over the body;
/// consecutive delivery failures accumulate and auto-disable the endpoint at the configured threshold.
/// The <see cref="Secret"/> is returned only once, in the creation response — never re-exposed.
/// </summary>
public sealed class WebhookEndpoint
{
    /// <summary>Surrogate ObjectId (string).</summary>
    public string Id { get; set; } = "";

    /// <summary>Owning API client id.</summary>
    public required string ClientId { get; set; }

    /// <summary>HTTPS-only delivery URL.</summary>
    public required string Url { get; set; }

    /// <summary>Signing secret (HMAC-SHA256 key). Never exposed after creation.</summary>
    public required string Secret { get; set; }

    /// <summary>Subscribed event names (subset of <see cref="WebhookEvents.All"/>).</summary>
    public List<string> Events { get; set; } = [];

    public bool Active { get; set; } = true;

    /// <summary>Consecutive non-2xx/failed deliveries; auto-disables the endpoint at the threshold.</summary>
    public int ConsecutiveFailures { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
