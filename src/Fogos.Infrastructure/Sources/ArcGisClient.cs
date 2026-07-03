using System.Text.Json;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Sources;

/// <summary>One page of ArcGIS features: raw attribute dictionaries plus the transfer-limit flag.</summary>
public sealed record ArcGisPage(
    IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Attributes,
    bool ExceededTransferLimit);

/// <summary>
/// ArcGIS FeatureServer query client (shell). Pages the OcorrenciasSite layer with <c>f=json</c> and
/// <c>resultOffset</c> (1000/page, legacy behaviour) and returns raw attribute dictionaries — Wave-2
/// jobs own the mapping into incidents.
/// </summary>
public sealed class ArcGisClient(HttpClient http, IOptions<FogosSourcesOptions> options)
{
    public const string HttpClientName = "arcgis";

    private ArcGisOptions Options => options.Value.ArcGis;

    /// <summary>Fetch a single page starting at <paramref name="resultOffset"/>.</summary>
    public async Task<ArcGisPage> QueryAsync(int resultOffset, int? pageSize = null, CancellationToken ct = default)
    {
        var count = pageSize ?? Options.PageSize;
        var url = $"{Options.FeatureServerUrl}/query?where=1%3D1&outFields=*&f=json&resultOffset={resultOffset}&resultRecordCount={count}";

        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var attributes = new List<IReadOnlyDictionary<string, JsonElement>>();
        if (root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
        {
            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("attributes", out var attrs))
                    continue;
                var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var prop in attrs.EnumerateObject())
                    dict[prop.Name] = prop.Value.Clone();
                attributes.Add(dict);
            }
        }

        var exceeded = root.TryGetProperty("exceededTransferLimit", out var flag)
                       && flag.ValueKind == JsonValueKind.True;
        return new ArcGisPage(attributes, exceeded);
    }

    /// <summary>Page through all features (follows <c>exceededTransferLimit</c> / full-page heuristic).</summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>> QueryAllAsync(CancellationToken ct = default)
    {
        var all = new List<IReadOnlyDictionary<string, JsonElement>>();
        var offset = 0;
        while (true)
        {
            var page = await QueryAsync(offset, ct: ct);
            all.AddRange(page.Attributes);
            if (!page.ExceededTransferLimit && page.Attributes.Count < Options.PageSize)
                break;
            if (page.Attributes.Count == 0)
                break;
            offset += Options.PageSize;
        }
        return all;
    }
}
