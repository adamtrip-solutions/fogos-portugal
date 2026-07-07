namespace Fogos.Infrastructure.Subscriptions;

/// <summary>
/// Redis subscription topic names shared by the Worker (publisher) and the Api
/// (subscriber). Both processes MUST use these exact strings or messages never meet.
/// </summary>
public static class SubscriptionTopics
{
    /// <summary>Every incident change is published here (drives <c>incidentUpdated</c> without an id).</summary>
    public const string IncidentFirehose = "incidents";

    /// <summary>Per-incident topic for <c>incidentUpdated(id:)</c>.</summary>
    public static string IncidentUpdated(string incidentId) => $"incidentUpdated:{incidentId}";

    /// <summary>Active-set delta stream for <c>activeIncidentsChanged</c>.</summary>
    public const string ActiveIncidentsChanged = "activeIncidentsChanged";
}
