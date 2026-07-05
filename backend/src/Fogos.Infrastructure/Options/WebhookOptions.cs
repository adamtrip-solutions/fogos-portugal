namespace Fogos.Infrastructure.Options;

/// <summary>Tunables for outbound webhooks: per-client cap, delivery timeout, and the auto-disable threshold.</summary>
public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>Maximum registered endpoints per API client.</summary>
    public int MaxEndpointsPerClient { get; set; } = 3;

    /// <summary>Consecutive failed deliveries that auto-disable an endpoint (with an ops notice).</summary>
    public int DisableThreshold { get; set; } = 10;

    /// <summary>Per-delivery HTTP timeout (seconds).</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
