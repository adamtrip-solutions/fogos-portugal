using System.Globalization;
using System.Text;

namespace Fogos.Worker.Jobs.Summaries;

/// <summary>
/// European-Portuguese copy for the twice-daily situation report (emoji-led, plain text). Composed from
/// live counters; kept here so the exact strings are auditable.
/// </summary>
public static class SituationReportCopy
{
    /// <summary>Human slot label for the report header.</summary>
    public static string SlotLabel(string slot) => slot == "morning" ? "manhã" : "noite";

    public static string Compose(
        string slot,
        string dateLabel,
        int activeFires,
        int man,
        int terrain,
        int aerial,
        int escalating,
        int warnings12h,
        long burnAreaHaYear,
        IReadOnlyList<(string Concelho, int Assets)> topFires)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"📋 Ponto de situação — {SlotLabel(slot)} de {dateLabel}\r\n\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"🔥 Incêndios ativos: {activeFires}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"🚨 Em escalada: {escalating}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"👩‍🚒 Operacionais: {man}  🚒 Veículos: {terrain}  🚁 Meios aéreos: {aerial}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"⚠️ Avisos nas últimas 12 h: {warnings12h}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"🌳 Área ardida no ano: {burnAreaHaYear} ha\r\n");

        if (topFires.Count > 0)
        {
            sb.Append("\r\nMaiores ocorrências:\r\n");
            foreach (var (concelho, assets) in topFires)
                sb.Append(CultureInfo.InvariantCulture, $" - {concelho}: {assets} meios\r\n");
        }

        return sb.ToString().TrimEnd();
    }
}
