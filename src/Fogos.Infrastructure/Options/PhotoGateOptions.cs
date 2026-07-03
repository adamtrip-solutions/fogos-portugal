namespace Fogos.Infrastructure.Options;

/// <summary>
/// Photo-upload abuse gates (MIGRATION-PLAN §2b). Tunable, but the defaults are the
/// deliberate anonymous-write exception for citizen photo submission.
/// </summary>
public sealed class PhotoGateOptions
{
    public const string SectionName = "PhotoGate";

    /// <summary>Uploads per IP per minute.</summary>
    public int PerIpPerMinute { get; set; } = 3;

    /// <summary>Uploads per incident, per IP, per hour.</summary>
    public int PerIncidentPerIpPerHour { get; set; } = 8;

    /// <summary>Uploads per incident, globally, per hour.</summary>
    public int PerIncidentPerHour { get; set; } = 80;

    /// <summary>Maximum photos awaiting moderation on a single incident.</summary>
    public int PendingPerIncident { get; set; } = 50;
}
