namespace Fogos.Domain.Locations;

public enum LocationLevel
{
    Distrito = 1,
    Concelho = 2,
}

/// <summary>Administrative geocoding table (legacy `locations`), with DICO precomputed.</summary>
public sealed class Location
{
    public string Id { get; set; } = "";
    public required LocationLevel Level { get; set; }

    /// <summary>ANEPC code for this unit.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    /// <summary>Zero-padded 4-char DICO (concelho rows only; "00" = Spain).</summary>
    public string? Dico { get; set; }

    /// <summary>
    /// True when this row was self-healed by the ingest coordinate fallback (a concelho name that missed the
    /// seeded table, back-filled from the polygon match) rather than seeded from the official geocoding table.
    /// Lets a later authoritative reseed distinguish inferred aliases from canonical rows.
    /// </summary>
    public bool Inferred { get; set; }
}
