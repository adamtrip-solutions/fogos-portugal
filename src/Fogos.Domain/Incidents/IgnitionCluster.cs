using Fogos.Domain.Geo;

namespace Fogos.Domain.Incidents;

/// <summary>
/// A spatial-temporal grouping of recent fire ignitions (single-linkage over a rolling window). The
/// member incident ids are the cluster's identity across runs; the centroid is their mean position.
/// Maintained by the cluster job with targeted upserts, never rewritten wholesale from the read side.
/// </summary>
public sealed class IgnitionCluster
{
    public string Id { get; set; } = "";

    /// <summary>The member incident ids (fires ignited within the window that link into this group).</summary>
    public required List<string> IncidentIds { get; set; }

    /// <summary>Mean position of the members.</summary>
    public required GeoPoint Centroid { get; set; }

    /// <summary>Earliest member ignition.</summary>
    public required DateTimeOffset FirstAt { get; set; }

    /// <summary>Latest member ignition — drives staleness (a cluster idle past the window deactivates).</summary>
    public required DateTimeOffset LastAt { get; set; }

    /// <summary>Distinct concelho names of the members.</summary>
    public List<string> Concelhos { get; set; } = [];

    public bool Active { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
