using Fogos.Domain.Geo;

namespace Fogos.Domain.Hotspots;

/// <summary>NASA FIRMS thermal hotspots for one incident (`_id` = incident id).</summary>
public sealed class Hotspots
{
    public required string IncidentId { get; set; }
    public List<HotspotSample> Viirs { get; set; } = [];
    public List<HotspotSample> Modis { get; set; } = [];
    public DateTimeOffset FetchedAt { get; set; }
}

public sealed record HotspotSample(GeoPoint Position, DateTimeOffset? AcquiredAt, double? Brightness, string? Confidence);
