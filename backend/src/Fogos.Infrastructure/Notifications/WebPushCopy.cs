using Fogos.Domain.Alerts;

namespace Fogos.Infrastructure.Notifications;

/// <summary>
/// European-Portuguese notification <b>titles</b> per alert kind. The body reuses the matcher's
/// <c>AlertCopy</c> string (passed through delivery), so this only owns the short title line and the
/// deep-link target the browser opens on click.
/// </summary>
public static class WebPushCopy
{
    /// <summary>The notification title for an alert kind (one of <see cref="AlertEventKind"/>).</summary>
    public static string Title(string kind) => kind switch
    {
        AlertEventKind.NewIncident => "Novo incêndio",
        AlertEventKind.Escalation => "Incêndio em agravamento",
        AlertEventKind.Rekindle => "Possível reacendimento",
        AlertEventKind.Risk => "Risco de incêndio elevado",
        _ => "Alerta Fogos.pt",
    };
}
