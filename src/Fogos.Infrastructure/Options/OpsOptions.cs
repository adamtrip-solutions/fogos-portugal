namespace Fogos.Infrastructure.Options;

/// <summary>Discord webhook endpoints for operational alerting and dry-run capture.</summary>
public sealed class OpsOptions
{
    public const string SectionName = "Ops";

    /// <summary>General channel: feed freshness, parser drift, job notices.</summary>
    public string? DiscordGeneralWebhook { get; set; }

    /// <summary>Errors channel: always delivered when configured.</summary>
    public string? DiscordErrorsWebhook { get; set; }

    /// <summary>Dry-run capture channel: what publishers would have sent.</summary>
    public string? DiscordDryRunWebhook { get; set; }
}
