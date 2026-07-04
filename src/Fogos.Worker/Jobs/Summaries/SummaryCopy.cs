namespace Fogos.Worker.Jobs.Summaries;

/// <summary>
/// Social copy for the hourly/daily summary jobs, ported verbatim (emoji, CRLF and spacing included) from
/// the legacy <c>HourlySummary</c> / <c>DailySummary</c> jobs. Kept in one place so the exact strings stay
/// auditable against the live platform. The woman glyph is 👩 + a zero-width joiner (U+200D), exactly as the
/// PHP source carried it; <c>https://fogos.pt</c> is hard-coded in the hourly suffix as in legacy.
/// </summary>
public static class SummaryCopy
{
    private const string Woman = "\U0001F469‍"; // 👩 + zero-width joiner, matching the legacy source byte-for-byte

    // ── Hourly ────────────────────────────────────────────────────────────────
    public static string HourlyNoActive(string hhmm) =>
        $"{hhmm} - Sem registo de incêndios ativos.";

    public static string HourlyActive(string hhmm, int total, int man, int cars, int areal) =>
        $"{hhmm} - {total} {Incendio(total)} em curso. Meios Mobilizados:\r\n{Woman} {man}\r\n🚒 {cars}\r\n🚁 {areal} \r\n";

    /// <summary>Suffix when no fires are in resolution (appended to the active block).</summary>
    public static string HourlyResolutionSuffixNone() =>
        " https://fogos.pt #FogosPT #Status";

    /// <summary>Suffix listing the in-resolution fires' mobilized means.</summary>
    public static string HourlyResolutionSuffix(int total, int man, int cars, int areal) =>
        $"{total} {Incendio(total)} em resolução. Meios Mobilizados:\r\n{Woman} {man}\r\n🚒 {cars}\r\n🚁 {areal} \r\n https://fogos.pt #FogosPT";

    // ── Daily ─────────────────────────────────────────────────────────────────
    public static string Daily(string date, int total, int maxMan, int maxCars, int maxPlanes, long burnAreaHa) =>
        $"ℹ Resumo diário de ontem {date}:\r\n - Total de ignições: {total} \r\n - Operacionais Mobilizados: {maxMan} \r\n - Veiculos Mobilizados: {maxCars} \r\n - Missões com Meios Aéreos: {maxPlanes} \r\n - Total Área Ardida contabilizada: {burnAreaHa} ha ℹ";

    private static string Incendio(int total) => total == 1 ? "Incêndio" : "Incêndios";
}
