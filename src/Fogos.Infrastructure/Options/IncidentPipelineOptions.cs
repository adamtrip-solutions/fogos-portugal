namespace Fogos.Infrastructure.Options;

/// <summary>Incident ingestion + social pipeline knobs.</summary>
public sealed class IncidentPipelineOptions
{
    public const string SectionName = "Incidents";

    /// <summary>Active ingester: <c>arcgis</c> (default, scheduled) or <c>anepc</c> (fallback).</summary>
    public string Source { get; set; } = "arcgis";

    /// <summary>Link domain used in social copy (legacy <c>SOCIAL_LINK_DOMAIN</c>).</summary>
    public string SocialLinkDomain { get; set; } = "fogosportugal.pt";

    /// <summary>The ArcGIS feed is considered stale after this long unchanged (legacy history.json idea).</summary>
    public TimeSpan FeedStaleAfter { get; set; } = TimeSpan.FromMinutes(30);
}
