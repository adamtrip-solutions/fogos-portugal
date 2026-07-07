using HotChocolate.Language;

namespace Fogos.Api.GraphQL.RateLimiting;

/// <summary>
/// Deterministic operation-cost proxy (MIGRATION-PLAN §2b fallback formula):
/// <c>cost = 10 + Σ (top-level field weight × pagination factor)</c>, where the pagination factor
/// for list fields is <c>max(1, first/25)</c>. Computed straight from the parsed operation document
/// so it is stable and testable, independent of HotChocolate's internal static-cost machinery.
/// </summary>
public static class GraphQLCostCalculator
{
    private const double BaseCost = 10;

    // Top-level query fields and their weights; list fields also scale with the requested page size.
    private static readonly Dictionary<string, (double Weight, string? PageArg)> Weights = new(StringComparer.Ordinal)
    {
        ["incidents"] = (5, "first"),
        ["activeIncidents"] = (5, null),
        ["aircraftTrack"] = (4, "limit"),
        ["aircraft"] = (3, null),
        ["stats"] = (3, null),
        ["dailyWeather"] = (2, null),
        ["weatherStations"] = (2, null),
        ["weatherWarnings"] = (2, null),
        ["temperatureWaves"] = (2, null),
        ["fireRisk"] = (2, null),
        ["incident"] = (1, null),
    };

    /// <summary>Returns the cost of a query string, or <see cref="BaseCost"/> when it cannot be parsed.</summary>
    public static double Compute(string query, string? operationName = null)
    {
        DocumentNode document;
        try
        {
            document = Utf8GraphQLParser.Parse(query);
        }
        catch
        {
            return BaseCost; // unparseable — HotChocolate will reject it; charge the base.
        }

        return Compute(document, operationName);
    }

    public static double Compute(DocumentNode document, string? operationName = null)
    {
        var operation = SelectOperation(document, operationName);
        if (operation is null)
            return BaseCost;

        var cost = BaseCost;
        foreach (var selection in operation.SelectionSet.Selections)
        {
            if (selection is not FieldNode field)
                continue;

            if (!Weights.TryGetValue(field.Name.Value, out var spec))
            {
                cost += 1; // unknown/other top-level field.
                continue;
            }

            var factor = spec.PageArg is null ? 1.0 : PageFactor(field, spec.PageArg);
            cost += spec.Weight * factor;
        }

        return cost;
    }

    private static double PageFactor(FieldNode field, string pageArg)
    {
        foreach (var arg in field.Arguments)
        {
            if (arg.Name.Value == pageArg && arg.Value is IntValueNode intValue && int.TryParse(intValue.Value, out var n))
                return Math.Max(1.0, n / 25.0);
        }
        return 1.0; // default page size (25) → factor 1.
    }

    private static OperationDefinitionNode? SelectOperation(DocumentNode document, string? operationName)
    {
        var operations = document.Definitions.OfType<OperationDefinitionNode>().ToList();
        if (operations.Count == 0)
            return null;
        if (operationName is not null)
            return operations.FirstOrDefault(o => o.Name?.Value == operationName) ?? operations[0];
        return operations[0];
    }
}
