using Fogos.Domain.Geo;

namespace Fogos.Domain.Aircraft;

/// <summary>
/// Append-only aircraft position sample. TTL-indexed — raw samples are noise after a
/// season (retention length is a Phase 4 decision).
/// </summary>
public sealed class FlightPosition
{
    public string Id { get; set; } = "";
    public required string Icao { get; set; }
    public required string Registration { get; set; }
    public required GeoPoint Position { get; set; }
    public double? Altitude { get; set; }
    public required DateTimeOffset SampledAt { get; set; }

    /// <summary>fr24 / adsbfi / airpaneslive — which provider sampled it.</summary>
    public required string Source { get; set; }

    public string? Fr24Id { get; set; }
}
