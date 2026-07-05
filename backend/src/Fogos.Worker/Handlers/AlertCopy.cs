namespace Fogos.Worker.Handlers;

/// <summary>
/// European-Portuguese copy for alert-subscription event messages (WP4). Kept in one place so every
/// user-facing string is auditable here.
/// </summary>
public static class AlertCopy
{
    public static string NewIncident(string place, string natureza) =>
        $"Novo incêndio em {place} — {natureza}.";

    public static string Escalation(string concelho, int assets) =>
        $"A ocorrência em {concelho} está em escalada: {assets} operacionais no terreno.";

    public static string Rekindle(string place, string natureza) =>
        $"Reacendimento em {place} — {natureza}.";

    public static string Risk(string concelhoName, string label) =>
        $"Risco de incêndio {label} hoje no concelho de {concelhoName}.";
}
