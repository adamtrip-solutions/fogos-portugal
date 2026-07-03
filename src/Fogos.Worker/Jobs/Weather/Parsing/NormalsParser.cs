using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fogos.Worker.Jobs.Weather.Parsing;

/// <summary>One station's monthly climate normals (Jan..Dec) parsed from the IPMA allstations blob.</summary>
public sealed record ParsedNormal(int StationId, string Name, double[] TmaxMean, double[] TminMean);

/// <summary>
/// Parses the <c>allstations = [ { … } ];</c> JS literal from an IPMA climate-normals page.
/// Port of <c>ImportWeatherNormals::importFromAllstations</c> (the non-PDF path — see the .NET
/// project note "scrapes the IPMA normals pages, no PDF parsing"): id = <c>NUM_AUT</c>, name =
/// <c>NOME</c>, and 12 monthly means from <c>MTX{MONTH}</c>/<c>MTN{MONTH}</c>. A station missing any
/// month is skipped (matching legacy's "return null → skip").
/// </summary>
public static class NormalsParser
{
    private static readonly string[] MonthCodes =
        ["JAN", "FEV", "MAR", "ABR", "MAI", "JUN", "JUL", "AGO", "SET", "OUT", "NOV", "DEZ"];

    private static readonly Regex AllStations =
        new(@"allstations\s*=\s*(\[\s*\{[\s\S]*?\}\s*\])\s*;", RegexOptions.Compiled);

    public static IReadOnlyList<ParsedNormal> Parse(string html)
    {
        var match = AllStations.Match(html);
        if (!match.Success)
            throw new FormatException("IPMA normals page: 'allstations' array not found.");

        using var doc = JsonDocument.Parse(match.Groups[1].Value);
        var normals = new List<ParsedNormal>();

        foreach (var s in doc.RootElement.EnumerateArray())
        {
            var id = IpmaJson.ReadInt(IpmaJson.Prop(s, "NUM_AUT"));
            var name = IpmaJson.ReadString(IpmaJson.Prop(s, "NOME"));
            if (id is null or 0 || name is null)
                continue;

            var tmax = ExtractMonthly(s, "MTX");
            var tmin = ExtractMonthly(s, "MTN");
            if (tmax is null || tmin is null)
                continue;

            normals.Add(new ParsedNormal(id.Value, name, tmax, tmin));
        }

        return normals;
    }

    private static double[]? ExtractMonthly(JsonElement row, string prefix)
    {
        var values = new double[12];
        for (var i = 0; i < MonthCodes.Length; i++)
        {
            var v = IpmaJson.ReadNumber(IpmaJson.Prop(row, prefix + MonthCodes[i]));
            if (v is null)
                return null;
            values[i] = v.Value;
        }
        return values;
    }
}
