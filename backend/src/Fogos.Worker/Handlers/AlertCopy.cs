namespace Fogos.Worker.Handlers;

/// <summary>
/// European-Portuguese copy for alert-subscription messages and their push titles (WP4). Kept beside
/// <see cref="IncidentCopy"/> / <see cref="SignalsCopy"/> so every user-facing string is auditable here.
/// </summary>
public static class AlertCopy
{
    // ── Event messages (shown in-app on poll) ────────────────────────────────────
    public static string NewIncident(string place, string natureza) =>
        $"Novo incêndio em {place} — {natureza}.";

    public static string Escalation(string concelho, int assets) =>
        $"A ocorrência em {concelho} está em escalada: {assets} operacionais no terreno.";

    public static string Rekindle(string place, string natureza) =>
        $"Reacendimento em {place} — {natureza}.";

    public static string Risk(string concelhoName, string label) =>
        $"Risco de incêndio {label} hoje no concelho de {concelhoName}.";

    // ── Push titles ──────────────────────────────────────────────────────────────
    public static string NewIncidentTitle(string concelho) => $"Novo incêndio — {concelho}";
    public static string EscalationTitle(string concelho) => $"Ocorrência em escalada — {concelho}";
    public static string RekindleTitle(string concelho) => $"Reacendimento — {concelho}";
    public static string RiskTitle(string concelhoName) => $"Risco de incêndio — {concelhoName}";
}
