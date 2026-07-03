namespace Fogos.Domain.Incidents;

/// <summary>The business thresholds that drive social/push escalation, ported verbatim.</summary>
public static class IncidentRules
{
    /// <summary>IMPORTANT_INCIDENT_TOTAL_ASSETS — aerial + terrain must exceed this.</summary>
    public const int ImportantIncidentTotalAssets = 15;

    /// <summary>An incident only qualifies as important once it is older than this.</summary>
    public static readonly TimeSpan ImportantIncidentMinAge = TimeSpan.FromHours(3);

    /// <summary>BIG_INCIDENT_MAN — man count at/above which the "big incident" post fires.</summary>
    public const int BigIncidentMan = 100;

    /// <summary>History/status side effects only apply to incidents from this year on.</summary>
    public const int HistoryMinYear = 2022;

    public static bool QualifiesAsImportant(Incident incident, DateTimeOffset now) =>
        incident.Active
        && incident.Kind == IncidentKind.Fire
        && IncidentStatusCatalog.ImportantCheckCodes.Contains(incident.Status.Code)
        && incident.Resources.TotalAssets > ImportantIncidentTotalAssets
        && now - incident.OccurredAt > ImportantIncidentMinAge;

    public static bool QualifiesAsBig(Incident incident) =>
        incident.Kind == IncidentKind.Fire && incident.Resources.Man >= BigIncidentMan;
}
