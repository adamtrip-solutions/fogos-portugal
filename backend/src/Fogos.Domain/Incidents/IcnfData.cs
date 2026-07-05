namespace Fogos.Domain.Incidents;

/// <summary>ICNF enrichment sub-document (burn area, cause taxonomy, KML perimeter presence).</summary>
public sealed record IcnfData
{
    public BurnArea? BurnArea { get; init; }

    /// <summary>Cause type, e.g. "Natural", "Negligente", "Intencional" (legacy icnf.tipocausa).</summary>
    public string? CauseType { get; init; }

    /// <summary>Cause family (legacy causafamilia).</summary>
    public string? CauseFamily { get; init; }

    /// <summary>Specific cause description.</summary>
    public string? Cause { get; init; }

    /// <summary>Species/vegetation name (legacy especieName).</summary>
    public string? SpeciesName { get; init; }

    /// <summary>Family name (legacy familiaName).</summary>
    public string? FamilyName { get; init; }

    /// <summary>Who raised the alert (legacy fonte alerta).</summary>
    public string? AlertSource { get; init; }

    /// <summary>ICNF's own occurrence id (ncco).</summary>
    public string? IcnfId { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>Burn area in hectares, split by land type (legacy icnf.burnArea).</summary>
public sealed record BurnArea(double? Povoamento, double? Agricola, double? Mato, double? Total);
