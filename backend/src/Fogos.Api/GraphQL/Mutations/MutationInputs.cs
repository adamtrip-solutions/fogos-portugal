using Fogos.Domain.Alerts;

namespace Fogos.Api.GraphQL.Mutations;

/// <summary>
/// Create an alert subscription. A Concelho subscription needs a <see cref="Dico"/>; a Point subscription
/// needs <see cref="Latitude"/>/<see cref="Longitude"/> + a <see cref="RadiusKm"/> (1–50). A risk alert is
/// requested by setting <see cref="RiskThreshold"/> (4 or 5).
/// </summary>
public sealed record CreateAlertSubscriptionInput
{
    public required AlertSubscriptionKind Kind { get; init; }
    public string? Dico { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? RadiusKm { get; init; }
    public int? RiskThreshold { get; init; }
}

/// <summary>
/// Operator POSIT ("ponto de situação") input. The resource counts overwrite the incident's committed
/// means (any omitted field keeps the current value); <see cref="Cos"/> / <see cref="Pco"/> / <see cref="Notes"/>
/// form the free-text situation report. Legacy <c>IncidentController::addPosit</c> only carried the narrative
/// (<c>posit</c>→extra, <c>cos</c>, <c>pco</c>); the clean schema also lets a POSIT report the means directly,
/// which is what drives the <c>IncidentResourcesChanged</c> escalation.
/// </summary>
public sealed record PositInput
{
    public int? Man { get; init; }
    public int? Terrain { get; init; }
    public int? Aerial { get; init; }
    public int? Aquatic { get; init; }
    public int? HeliFight { get; init; }
    public int? HeliCoord { get; init; }
    public int? PlaneFight { get; init; }

    /// <summary>Comandante de Operações de Socorro.</summary>
    public string? Cos { get; init; }

    /// <summary>Posto de Comando Operacional.</summary>
    public string? Pco { get; init; }

    /// <summary>Situation narrative (legacy <c>posit</c> → incident <c>extra</c>).</summary>
    public string? Notes { get; init; }
}

/// <summary>Operator broadcast-warning input (legacy WarningsController carried only the message text).</summary>
public sealed record WarningInput
{
    public required string Message { get; init; }

    /// <summary>Optional link appended to the broadcast copy.</summary>
    public string? Url { get; init; }
}
