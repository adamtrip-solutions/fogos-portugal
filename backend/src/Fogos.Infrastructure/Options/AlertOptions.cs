namespace Fogos.Infrastructure.Options;

/// <summary>
/// Tunables for anonymous alert subscriptions: the per-IP creation rate gate, the point-radius cap,
/// the inactivity purge window, and the Portugal bounding box a Point subscription must fall inside.
/// </summary>
public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>Max subscription creations per IP per minute (anonymous abuse gate).</summary>
    public int CreatePerIpPerMinute { get; set; } = 5;

    /// <summary>Max subscription creations per IP per day.</summary>
    public int CreatePerIpPerDay { get; set; } = 50;

    /// <summary>Maximum allowed match radius for a Point subscription (km).</summary>
    public double MaxRadiusKm { get; set; } = 50;

    /// <summary>Purge subscriptions older than this with no poll activity in the same window.</summary>
    public int PurgeAfterDays { get; set; } = 90;

    // ── Portugal bounding box (mainland + islands) ───────────────────────────────
    public double MinLatitude { get; set; } = 29.5;
    public double MaxLatitude { get; set; } = 42.5;
    public double MinLongitude { get; set; } = -32.5;
    public double MaxLongitude { get; set; } = -6.0;

    /// <summary>True when a point falls inside the configured Portugal bounding box.</summary>
    public bool InPortugal(double latitude, double longitude) =>
        latitude >= MinLatitude && latitude <= MaxLatitude
        && longitude >= MinLongitude && longitude <= MaxLongitude;
}
