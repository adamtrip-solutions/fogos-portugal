namespace Fogos.Domain.Incidents;

/// <summary>
/// Replaces the legacy isFire / isUrbanFire / isTransporteFire / isOtherFire /
/// isOtherIncident / isFMA boolean spread. Exactly one kind per incident.
/// </summary>
public enum IncidentKind
{
    /// <summary>Rural/forest fire — the product's core (legacy isFire).</summary>
    Fire,

    /// <summary>Urban/building fire (legacy isUrbanFire).</summary>
    UrbanFire,

    /// <summary>Vehicle/transport fire (legacy isTransporteFire).</summary>
    TransportFire,

    /// <summary>Other fires: garbage, equipment, industry (legacy isOtherFire).</summary>
    OtherFire,

    /// <summary>Adverse meteorological phenomena — floods, storm damage (legacy isFMA).</summary>
    Fma,

    /// <summary>Anything else ANEPC publishes (legacy isOtherIncident).</summary>
    Other,
}
