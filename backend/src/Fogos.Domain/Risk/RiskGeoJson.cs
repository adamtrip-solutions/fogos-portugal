namespace Fogos.Domain.Risk;

/// <summary>
/// Pre-built concelho-polygon GeoJSON payload per forecast horizon (legacy `rcmJS`),
/// served verbatim by the risk map endpoints.
/// </summary>
public sealed class RiskGeoJson
{
    public string Id { get; set; } = "";
    public required RiskDay When { get; set; }

    /// <summary>Date the forecast applies to.</summary>
    public required DateOnly ForecastDate { get; set; }

    /// <summary>When IPMA ran the model.</summary>
    public DateTimeOffset? RunAt { get; set; }

    /// <summary>Raw GeoJSON FeatureCollection (string — served as-is, never parsed back).</summary>
    public required string GeoJson { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
