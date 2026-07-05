namespace Fogos.Domain.Aircraft;

/// <summary>
/// Association between a tracked aircraft (by ICAO hex) and an incident, derived by the
/// <c>AircraftAssociationJob</c> from recent flight positions loitering near an active fire.
/// One document per (incident, aircraft); <c>Active</c> flips off when the aircraft stops being seen.
/// </summary>
public sealed class IncidentAircraftLink
{
    /// <summary>Surrogate ObjectId (string).</summary>
    public string Id { get; set; } = "";

    public required string IncidentId { get; set; }

    /// <summary>ICAO hex of the tracked aircraft (joins <c>tracked_aircraft._id</c>).</summary>
    public required string Icao { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>How many association observations backed this link (bumped once per matched run).</summary>
    public int Samples { get; set; }

    public bool Active { get; set; }
}
