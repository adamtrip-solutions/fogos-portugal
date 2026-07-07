namespace Fogos.Infrastructure.Options;

/// <summary>How the Web Push channel behaves: capture-only (DryRun) or actually POST to push services (Live).</summary>
public enum WebPushMode
{
    /// <summary>Capture the payload to the ops dry-run channel; send no HTTP. The safe default.</summary>
    DryRun,

    /// <summary>POST the encrypted payload to the browser's push service.</summary>
    Live,
}

/// <summary>
/// Tunables for the Web Push delivery channel (VAPID). Empty by default: with no <see cref="PublicKey"/>
/// the feature is inert (registration errors WEB_PUSH_DISABLED) and — per house rule — a publisher never
/// goes live without explicit config, so <see cref="Mode"/> defaults to <see cref="WebPushMode.DryRun"/>.
/// </summary>
public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    /// <summary>VAPID subject — a <c>mailto:</c> (or https) contact the push service can reach.</summary>
    public string? Subject { get; set; }

    /// <summary>VAPID public key (base64url uncompressed P-256 point). Also handed to the browser to subscribe.</summary>
    public string? PublicKey { get; set; }

    /// <summary>VAPID private key (base64url P-256 <c>d</c> scalar). Secret — never exposed over the API.</summary>
    public string? PrivateKey { get; set; }

    /// <summary>Delivery mode; DryRun by default (no live sends without explicit config).</summary>
    public WebPushMode Mode { get; set; } = WebPushMode.DryRun;

    /// <summary>Consecutive failed deliveries that auto-disable a device (with an ops notice).</summary>
    public int DisableThreshold { get; set; } = 10;

    /// <summary>Per-delivery HTTP timeout (seconds).</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Disable/purge devices unseen for this many days (fallback CreatedAt) or already disabled.</summary>
    public int PurgeAfterDays { get; set; } = 180;

    /// <summary>Max device registrations per IP per minute (anonymous abuse gate).</summary>
    public int RegisterPerIpPerMinute { get; set; } = 5;

    /// <summary>Max device registrations per IP per day.</summary>
    public int RegisterPerIpPerDay { get; set; } = 50;

    /// <summary>
    /// Allow-listed push-service hosts (SSRF guard — the worker POSTs to a registered endpoint). Suffix
    /// match, so regional prefixes (e.g. <c>fcm.googleapis.com</c>, <c>push.services.mozilla.com</c>
    /// subdomains) are covered. Defaults to the real browser push services.
    /// </summary>
    public string[] AllowedEndpointHosts { get; set; } =
    [
        "fcm.googleapis.com",
        "updates.push.services.mozilla.com",
        "web.push.apple.com",
        "notify.windows.com",
    ];

    /// <summary>True once a VAPID public key is configured — the switch that turns registration on.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey);
}
