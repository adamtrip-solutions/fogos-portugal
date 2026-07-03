using System.Text.RegularExpressions;

namespace Fogos.Worker.Jobs.Icnf;

/// <summary>One row of the ICNF <c>faztable.asp</c> occurrences table: the numeric id + normalized status.</summary>
public sealed record IcnfTableRow(string Id, string StatusLabel);

/// <summary>
/// Parses the ICNF <c>faztable.asp</c> response (ProcessICNFNewFireData.php): a JS array embedded in HTML.
/// Ports the exact legacy shape — strip newlines, match every <c>[...]</c> group, skip the first two
/// header rows, split each row on <c>',</c>, take field 0 as the id (tags/quotes stripped, digits only)
/// and field 12 as the ICNF state, mapped to a canonical status label.
/// </summary>
public static partial class IcnfTableParser
{
    [GeneratedRegex(@"\[(.*?)\]")]
    private static partial Regex RowPattern();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagPattern();

    public static IReadOnlyList<IcnfTableRow> Parse(string html)
    {
        var flattened = html.Replace("\r", "").Replace("\n", "");
        var rows = new List<IcnfTableRow>();

        var index = 0;
        foreach (Match match in RowPattern().Matches(flattened))
        {
            var current = index++;
            if (current is 0 or 1)
                continue; // legacy skips the first two rows (headers)

            var fields = match.Groups[1].Value.Split("',");
            if (fields.Length == 0)
                continue;

            var id = TagPattern().Replace(fields[0].Replace("'", ""), "").Trim();
            if (id.Length == 0 || !id.All(char.IsDigit))
                continue;

            var estado = fields.Length > 12 ? fields[12].TrimStart('\'').Trim() : "";
            rows.Add(new IcnfTableRow(id, MapStatus(estado)));
        }

        return rows;
    }

    /// <summary>ICNF state → canonical status label (Extinto→Conclusão, Dominado→Em Resolução, else Em Curso).</summary>
    public static string MapStatus(string icnfEstado) => icnfEstado switch
    {
        "Extinto" => "Conclusão",
        "Dominado" => "Em Resolução",
        _ => "Em Curso",
    };
}
