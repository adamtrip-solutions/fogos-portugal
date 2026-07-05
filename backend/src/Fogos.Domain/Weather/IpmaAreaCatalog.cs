using System.Globalization;
using System.Text;

namespace Fogos.Domain.Weather;

/// <summary>
/// Maps IPMA awareness-area codes (<c>idAreaAviso</c>, district-level — the code stored on
/// <see cref="WeatherWarning.AreaCode"/>) to Portuguese district names, and back. Covers all 18
/// mainland districts plus the Madeira and Açores island areas. Source: the IPMA awareness-area
/// catalog (api.ipma.pt distrits-islands warning areas). District matching is accent/case-insensitive
/// so it lines up with whatever casing the incident/location tables carry.
/// </summary>
public static class IpmaAreaCatalog
{
    private static readonly IReadOnlyDictionary<string, string> AreaToDistrictMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Mainland (18 districts) ──────────────────────────────────────────
            ["AVR"] = "Aveiro",
            ["BJA"] = "Beja",
            ["BGC"] = "Bragança",
            ["BRG"] = "Braga",
            ["CBR"] = "Coimbra",
            ["CTB"] = "Castelo Branco",
            ["EVR"] = "Évora",
            ["FAR"] = "Faro",
            ["GDA"] = "Guarda",
            ["LRA"] = "Leiria",
            ["LSB"] = "Lisboa",
            ["PTG"] = "Portalegre",
            ["PTO"] = "Porto",
            ["STR"] = "Santarém",
            ["STB"] = "Setúbal",
            ["VCT"] = "Viana do Castelo",
            ["VRL"] = "Vila Real",
            ["VIS"] = "Viseu",
            // ── Madeira ──────────────────────────────────────────────────────────
            ["MCN"] = "Madeira",
            ["MCS"] = "Madeira",
            ["MMT"] = "Madeira",
            ["PSA"] = "Madeira",
            // ── Açores (three groups) ────────────────────────────────────────────
            ["AOC"] = "Açores",
            ["ACE"] = "Açores",
            ["AOR"] = "Açores",
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DistrictToAreasMap =
        AreaToDistrictMap
            .GroupBy(kv => Normalize(kv.Value))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(kv => kv.Key).ToList());

    /// <summary>Every mainland + island area code known to the catalog.</summary>
    public static IReadOnlyCollection<string> AreaCodes => (IReadOnlyCollection<string>)AreaToDistrictMap.Keys;

    /// <summary>District name for an IPMA area code, or null when unknown.</summary>
    public static string? District(string areaCode) =>
        AreaToDistrictMap.TryGetValue(areaCode.Trim(), out var d) ? d : null;

    /// <summary>The IPMA area code(s) covering a district (accent/case-insensitive); empty when unknown.</summary>
    public static IReadOnlyList<string> AreaCodesForDistrict(string district) =>
        DistrictToAreasMap.TryGetValue(Normalize(district), out var codes) ? codes : [];

    /// <summary>Lower-cases and strips diacritics so "Viana Do Castelo" and "Viana do Castelo" match.</summary>
    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
