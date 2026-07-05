namespace Fogos.Infrastructure.Options;

/// <summary>
/// Firebase Cloud Messaging options. Empty by default; the "fcm" publisher mode still gates whether
/// anything is actually sent (default DryRun). Credentials come from a service-account JSON file or
/// an inline JSON string; when neither is set the sender stays inert.
/// </summary>
public sealed class FcmOptions
{
    public const string SectionName = "Fcm";

    /// <summary>Path to the service-account JSON (legacy hardcoded <c>/var/www/html/credentials.json</c>).</summary>
    public string? CredentialsPath { get; set; }

    /// <summary>Inline service-account JSON (alternative to a path; useful in container secrets).</summary>
    public string? CredentialsJson { get; set; }

    /// <summary>Firebase project id (legacy: <c>admob-app-id-6663345165</c>).</summary>
    public string? ProjectId { get; set; }

    /// <summary>Emit the legacy <c>web-/mobile-android-/mobile-ios-</c> topic variants alongside the canonical ones.</summary>
    public bool LegacyTopicsEnabled { get; set; }

    /// <summary>Prepended to every notification title (legacy prepended "Fogos.pt - ").</summary>
    public string TitlePrefix { get; set; } = "FogosPortugal - ";

    /// <summary>Delay before a scheduled push is delivered (legacy 3-minute debounce).</summary>
    public TimeSpan PushDelay { get; set; } = TimeSpan.FromMinutes(3);
}
