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
}
