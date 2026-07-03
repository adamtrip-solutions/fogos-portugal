namespace Fogos.Domain.Incidents;

/// <summary>Operational means committed to an incident (legacy man/terrain/aerial + POSIT breakdowns).</summary>
public sealed record Resources
{
    public int Man { get; init; }
    public int Terrain { get; init; }
    public int Aerial { get; init; }

    /// <summary>Aquatic means (legacy meios_aquaticos).</summary>
    public int Aquatic { get; init; }

    /// <summary>Firefighting helicopters (from ANEPC POSIT detail).</summary>
    public int HeliFight { get; init; }

    /// <summary>Coordination helicopters.</summary>
    public int HeliCoord { get; init; }

    /// <summary>Firefighting planes.</summary>
    public int PlaneFight { get; init; }

    public int TotalAssets => Aerial + Terrain;

    public static readonly Resources Zero = new();
}
