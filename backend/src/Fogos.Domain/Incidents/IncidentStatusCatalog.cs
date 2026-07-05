using System.Collections.Frozen;

namespace Fogos.Domain.Incidents;

/// <summary>
/// Canonical status vocabulary, ported verbatim from the legacy platform
/// (app/Models/Incident.php STATUS_ID / STATUS_COLORS and the ingester alias maps).
/// The clean schema stores only the canonical (code, label); every dirty inbound
/// variant is normalized here, at the edge.
/// </summary>
public static class IncidentStatusCatalog
{
    public const int Despacho = 3;
    public const int DespachoPrimeiroAlerta = 4;
    public const int EmCurso = 5;
    public const int ChegadaAoTeatroDeOperacoes = 6;
    public const int EmResolucao = 7;
    public const int Conclusao = 8;
    public const int Vigilancia = 9;
    public const int Encerrada = 10;
    public const int FalsoAlarme = 11;
    public const int FalsoAlerta = 12;

    /// <summary>
    /// Terminal code for an active incident closed out by the feed-drop sweep (dropped from the ANEPC
    /// feed past the grace window). Distinct from operator-driven <see cref="Encerrada"/> so the reason
    /// ("no further updates") stays legible; shares Encerrada's green color family.
    /// </summary>
    public const int EncerradaSemAtualizacao = 13;

    /// <summary>Display labels per code. 4 renders without the abbreviation dot ("1º"), matching the legacy display fix.</summary>
    private static readonly FrozenDictionary<int, string> Labels = new Dictionary<int, string>
    {
        [Despacho] = "Despacho",
        [DespachoPrimeiroAlerta] = "Despacho de 1º Alerta",
        [EmCurso] = "Em Curso",
        [ChegadaAoTeatroDeOperacoes] = "Chegada ao TO",
        [EmResolucao] = "Em Resolução",
        [Conclusao] = "Conclusão",
        [Vigilancia] = "Vigilância",
        [Encerrada] = "Encerrada",
        [FalsoAlarme] = "Falso Alarme",
        [FalsoAlerta] = "Falso Alerta",
        [EncerradaSemAtualizacao] = "Encerrada (sem atualização)",
    }.ToFrozenDictionary();

    /// <summary>
    /// Colors as the live ingesters resolved them (label-keyed STATUS_COLORS — the code-keyed
    /// entries in the legacy table were a dead path for feed-ingested incidents).
    /// </summary>
    private static readonly FrozenDictionary<int, string> Colors = new Dictionary<int, string>
    {
        [Despacho] = "FF6E02",
        [DespachoPrimeiroAlerta] = "FF6E02",
        [EmCurso] = "B81E1F",
        [ChegadaAoTeatroDeOperacoes] = "B81E1F",
        [EmResolucao] = "6ABF59",
        [Conclusao] = "BDBDBD",
        [Vigilancia] = "6ABF59",
        [Encerrada] = "6ABF59",
        [FalsoAlarme] = "BDBDBD",
        [FalsoAlerta] = "BDBDBD",
        [EncerradaSemAtualizacao] = "6ABF59", // same green family as Encerrada
    }.ToFrozenDictionary();

    /// <summary>
    /// Inbound label → code, including every dirty variant the feeds have historically sent:
    /// STATUS_ID keys, ProcessOcorrenciasSite STATUS_LOOKUP_ALIASES, and the two
    /// whitespace-damaged keys from STATUS_COLORS. Lookup trims and case-folds first.
    /// </summary>
    private static readonly FrozenDictionary<string, int> LabelLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Despacho"] = Despacho,
        ["Despacho de 1.º Alerta"] = DespachoPrimeiroAlerta,
        ["Despacho de 1º Alerta"] = DespachoPrimeiroAlerta,
        ["DESPACHO DE 1º ALERTA"] = DespachoPrimeiroAlerta,
        ["Em Curso"] = EmCurso,
        ["Chegada ao TO"] = ChegadaAoTeatroDeOperacoes,
        ["Em Resolução"] = EmResolucao,
        ["Conclusão"] = Conclusao,
        ["Em Conclusão"] = Conclusao,
        ["Vigilância"] = Vigilancia,
        ["Encerrada"] = Encerrada,
        ["Falso Alarme"] = FalsoAlarme,
        ["Falso Alerta"] = FalsoAlerta,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Legacy ACTIVE_STATUS_CODES — drives the `active` flag and the active feeds.</summary>
    public static readonly IReadOnlySet<int> ActiveCodes = new HashSet<int> { 3, 4, 5, 6 };

    public static readonly IReadOnlySet<int> InactiveCodes = new HashSet<int> { 7, 8, 9, 10, 11, 12, 13 };

    /// <summary>
    /// CheckImportantFireIncident deliberately used a wider window (1–6) than ActiveCodes.
    /// Preserved as-is: important detection must consider pre-dispatch codes.
    /// </summary>
    public static readonly IReadOnlySet<int> ImportantCheckCodes = new HashSet<int> { 1, 2, 3, 4, 5, 6 };

    public static bool TryNormalize(string rawLabel, out IncidentStatus status)
    {
        var key = rawLabel.Trim();
        if (LabelLookup.TryGetValue(key, out var code))
        {
            status = FromCode(code);
            return true;
        }
        status = null!;
        return false;
    }

    public static IncidentStatus FromCode(int code) =>
        new(code, Labels.TryGetValue(code, out var label) ? label : $"Desconhecido ({code})");

    public static string ColorFor(int code) => Colors.GetValueOrDefault(code, "BDBDBD");

    public static bool IsActive(int code) => ActiveCodes.Contains(code);
}
