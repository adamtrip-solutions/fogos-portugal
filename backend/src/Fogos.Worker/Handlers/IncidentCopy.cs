using Fogos.Domain.Incidents;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Push copy for incident notifications, ported verbatim (emoji, spacing and all) from the legacy jobs
/// (CheckImportantFireIncident, SaveIncidentHistory, SaveIncidentStatusHistory). Kept in one place so the
/// exact strings are auditable against the live platform.
/// </summary>
public static class IncidentCopy
{
    /// <summary>CheckImportantFireIncident push body.</summary>
    public static string ImportantPush(Incident i) =>
        $"ℹ🔥 Segundo os critérios da ANEPC o incêndio em {i.Location} é considerado importante 🔥ℹ";

    /// <summary>SaveIncidentHistory big-incident push body.</summary>
    public static string BigPush(Incident i) =>
        $"ℹ🚨 {i.Location} - Grande mobilização de meios:  👩‍🚒 {i.Resources.Man} 🚒 {i.Resources.Terrain} 🚁 {i.Resources.Aerial} 🚨ℹ";

    /// <summary>NotificationTool status-change push body.</summary>
    public static string StatusPush(string previousLabel, string currentLabel) =>
        $"Alteração de estado: de {previousLabel} para {currentLabel}";
}
