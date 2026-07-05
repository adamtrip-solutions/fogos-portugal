namespace Fogos.Domain.Warnings;

public enum WarningKind
{
    /// <summary>Operator-issued broadcast (legacy `warning`).</summary>
    Manual,

    /// <summary>AGIF/rural-fire management warning (legacy `warning_agif`).</summary>
    Agif,

    /// <summary>Site banner warning (legacy `warningSite`).</summary>
    Site,
}

/// <summary>
/// Unified broadcast warning — the three legacy collections differed only in fan-out,
/// so one collection with a kind discriminator.
/// </summary>
public sealed class Warning
{
    public string Id { get; set; } = "";
    public required WarningKind Kind { get; set; }
    public required string Message { get; set; }

    /// <summary>Optional link target.</summary>
    public string? Url { get; set; }

    /// <summary>Who issued it (operator credential name).</summary>
    public string? IssuedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
