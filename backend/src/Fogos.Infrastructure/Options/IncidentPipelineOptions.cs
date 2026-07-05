namespace Fogos.Infrastructure.Options;

/// <summary>Incident ingestion pipeline knobs.</summary>
public sealed class IncidentPipelineOptions
{
    public const string SectionName = "Incidents";

    /// <summary>Active ingester: <c>arcgis</c> (default, scheduled) or <c>anepc</c> (fallback).</summary>
    public string Source { get; set; } = "arcgis";

    /// <summary>The ArcGIS feed is considered stale after this long unchanged (legacy history.json idea).</summary>
    public TimeSpan FeedStaleAfter { get; set; } = TimeSpan.FromMinutes(30);
}
