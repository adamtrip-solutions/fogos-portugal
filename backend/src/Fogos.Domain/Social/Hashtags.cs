using System.Text.RegularExpressions;

namespace Fogos.Domain.Social;

/// <summary>
/// Hashtag generation, ported verbatim from HashTagTool: strip whitespace and hyphens,
/// keep accents and casing ("Viana do Castelo" → "#IRVianadoCastelo").
/// </summary>
public static partial class Hashtags
{
    [GeneratedRegex(@"\s+|\-")]
    private static partial Regex Separators();

    /// <summary>Incident-report tag used by the main fogos.pt account.</summary>
    public static string ForConcelho(string concelho) => "#IR" + Separators().Replace(concelho, "");

    /// <summary>Plain variant used by the Emergencias account.</summary>
    public static string Plain(string concelho) => "#" + Separators().Replace(concelho, "");
}
