namespace Fogos.Domain.Risk;

public enum RiskDay
{
    Today,
    Tomorrow,
    After,
}

/// <summary>IPMA RCM fire-risk levels 1–5 with the legacy PT labels and emoji.</summary>
public static class RiskLevels
{
    private static readonly string[] Labels = ["", "Reduzido", "Moderado", "Elevado", "Muito Elevado", "Máximo"];
    private static readonly string[] Emoji = ["", "🟢", "🔵", "🟡", "🟠", "🔴"];

    public static string Label(int level) => level is >= 1 and <= 5 ? Labels[level] : "Desconhecido";
    public static string ToEmoji(int level) => level is >= 1 and <= 5 ? Emoji[level] : "❔";
}

/// <summary>
/// Fire risk per concelho per forecast run (legacy `rcm`). Upsert key (Dico, Date).
/// Levels are 1–5; a missing horizon is null.
/// </summary>
public sealed class ConcelhoRisk
{
    public string Id { get; set; } = "";
    public required string Dico { get; set; }
    public required string Concelho { get; set; }

    /// <summary>Forecast run date (Lisbon calendar day the run refers to as "today").</summary>
    public required DateOnly Date { get; set; }

    public int? Today { get; set; }
    public int? Tomorrow { get; set; }
    public int? After { get; set; }
    public int? After2 { get; set; }
    public int? After3 { get; set; }

    public int? For(RiskDay day) => day switch
    {
        RiskDay.Today => Today,
        RiskDay.Tomorrow => Tomorrow,
        _ => After,
    };
}
