namespace Fogos.Infrastructure.Options;

/// <summary>Incident ingestion pipeline knobs.</summary>
public sealed class IncidentPipelineOptions
{
    public const string SectionName = "Incidents";

    /// <summary>Active ingester: <c>arcgis</c> (default, scheduled) or <c>anepc</c> (fallback).</summary>
    public string Source { get; set; } = "arcgis";

    /// <summary>
    /// Whole-feed freshness gate: the ArcGIS feed is considered stale after this long unchanged (legacy
    /// history.json idea), which fires the "feed not updating" ops alert and blocks the close-out sweep
    /// (a frozen feed can't be trusted to mean an incident really ended). Freshness signal only —
    /// per-incident grace lives in <see cref="CloseAfterMissingFor"/>.
    /// </summary>
    public TimeSpan FeedStaleAfter { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Per-incident grace before the close-out sweep terminates an active incident that has dropped off
    /// the feed. An active incident absent this long (measured from <c>LastSeenInFeedAt</c>, falling back
    /// to <c>CreatedAt</c>) is closed out to status 13. Defaults to <see cref="FeedStaleAfter"/>'s 30 min.
    /// </summary>
    public TimeSpan CloseAfterMissingFor { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Truncated-feed guard: if a single sweep's close-out candidates exceed
    /// <c>max(3, MaxCloseFraction × current active count)</c>, the whole sweep is aborted and ops alerted
    /// rather than mass-closing incidents on a bad/partial feed.
    /// </summary>
    public double MaxCloseFraction { get; set; } = 0.25;
}
