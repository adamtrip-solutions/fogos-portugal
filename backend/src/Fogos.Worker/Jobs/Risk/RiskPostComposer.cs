using System.Globalization;
using System.Text;
using Fogos.Domain.Risk;

namespace Fogos.Worker.Jobs.Risk;

/// <summary>
/// Composes the Portuguese fire-risk social posts. Pure and fixture-testable. Mirrors the legacy
/// <c>ProcessRCM::publishSocial</c> intent — a per-day summary of the concelhos at the top risk bands,
/// with the mandatory queimadas warning when any concelho is at Muito Elevado / Máximo — but as one
/// consolidated post per horizon rather than the legacy per-band tweet spray.
/// </summary>
public static class RiskPostComposer
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    private const string QueimadasWarning =
        "Nos dias de perigo “muito elevado” ou “máximo” é PROIBIDO fazer Queimadas. " +
        "Nos restantes dias apenas é PERMITIDO fazer com AUTORIZAÇÃO do município. " +
        "Faça o registo na APLICAÇÃO, é OBRIGATÓRIO. Aplicável nos territórios rurais e urbanos";

    /// <summary>Legacy PS/PR-project one-liner: "Risco de incêndio para hoje: {emoji} {label}".</summary>
    public static string ProjectRiskToday(int level) =>
        $"Risco de incêndio para hoje: {RiskLevels.ToEmoji(level)} {RiskLevels.Label(level)}";

    /// <summary>
    /// Builds the risk-map summary post for a horizon. <paramref name="tomorrow"/> selects the AMANHÃ
    /// wording; <paramref name="concelhoLevels"/> is (concelho name, 1–5 level) for that horizon.
    /// </summary>
    public static string ComposeRiskMap(DateOnly forecastDate, bool tomorrow, IEnumerable<(string Concelho, int Level)> concelhoLevels)
    {
        var when = tomorrow ? "AMANHÃ" : "hoje";
        var maximo = new List<string>();
        var muitoElevado = new List<string>();

        foreach (var (concelho, level) in concelhoLevels)
        {
            if (level == 5) maximo.Add(concelho);
            else if (level == 4) muitoElevado.Add(concelho);
        }
        maximo.Sort(StringComparer.Create(Pt, ignoreCase: false));
        muitoElevado.Sort(StringComparer.Create(Pt, ignoreCase: false));

        var date = forecastDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append(date).Append(" Risco de incêndio para ").Append(when).Append(" #FogosPT");

        if (maximo.Count == 0 && muitoElevado.Count == 0)
        {
            sb.Append('\n').Append("Sem registo de concelhos com risco Máximo ou Muito Elevado.");
            return sb.ToString();
        }

        if (maximo.Count > 0)
            sb.Append('\n').Append(RiskLevels.ToEmoji(5)).Append(" Máximo: ").Append(string.Join(", ", maximo));
        if (muitoElevado.Count > 0)
            sb.Append('\n').Append(RiskLevels.ToEmoji(4)).Append(" Muito Elevado: ").Append(string.Join(", ", muitoElevado));

        sb.Append('\n').Append(QueimadasWarning);
        return sb.ToString();
    }
}
