namespace Fogos.Infrastructure.Options;

/// <summary>
/// Tunables for the aircraft↔incident association job. Defaults match the WP2 spec; ops can override
/// any of them under the <c>AircraftAssociation</c> config section.
/// </summary>
public sealed class AircraftAssociationOptions
{
    public const string SectionName = "AircraftAssociation";

    /// <summary>How far back to look for flight positions when matching (minutes).</summary>
    public int LookbackMinutes { get; set; } = 15;

    /// <summary>Minimum number of positions in the lookback window for an aircraft to count as tracked.</summary>
    public int MinSamples { get; set; } = 2;

    /// <summary>An aircraft's latest position must be within this radius of the fire (km).</summary>
    public double RadiusKm { get; set; } = 7;

    /// <summary>A link whose LastSeenAt is older than this is deactivated (minutes).</summary>
    public int StaleMinutes { get; set; } = 20;
}
