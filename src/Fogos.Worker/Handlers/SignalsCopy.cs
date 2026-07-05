namespace Fogos.Worker.Handlers;

/// <summary>
/// European-Portuguese push copy for incident-signals notifications (WP1). Kept beside
/// <see cref="IncidentCopy"/> so every user-facing string is auditable in one place.
/// </summary>
public static class SignalsCopy
{
    /// <summary>Escalation push title.</summary>
    public static string EscalationPushTitle(string concelho) => $"Ocorrência em escalada — {concelho}";

    /// <summary>Escalation push body.</summary>
    public static string EscalationPushBody(string location, int assets) =>
        $"Meios a aumentar rapidamente em {location}: {assets} operacionais e veículos no terreno.";
}
