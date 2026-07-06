namespace Fogos.Worker.Jobs.Icnf;

/// <summary>
/// Normalizes the ICNF <c>INE</c> field into the canonical 4-char DICO (district 2 + concelho 2) used
/// everywhere else. ICNF publishes INE as D+CC+FF with the <b>district left unpadded</b>, so a raw INE is
/// 5 chars (1-digit district) or 6 chars (2-digit district): "60322" → "0603" (Coimbra), "100110" → "1001"
/// (Alcobaça). The old path stored INE verbatim, so these leaked as malformed dico ("60322") that silently
/// broke fireRisk / concelhoProfile lookups. A 4-char INE is already a valid 2+2 DICO and passes through
/// ("1408"). Anything else (empty, non-numeric, other lengths) returns null so the caller leaves
/// PreResolvedDico unset and the LocationResolver name/polygon path takes over — never store garbage.
/// </summary>
public static class IcnfDico
{
    public static string? FromIne(string? ine)
    {
        var s = ine?.Trim();
        if (string.IsNullOrEmpty(s) || !s.All(char.IsAsciiDigit))
            return null;

        return s.Length switch
        {
            4 => s,             // already a 2+2 DICO
            5 => "0" + s[..3],  // D + CC (+ FF dropped) → pad district to 2
            6 => s[..4],        // DD + CC (+ FF dropped)
            _ => null,
        };
    }
}
