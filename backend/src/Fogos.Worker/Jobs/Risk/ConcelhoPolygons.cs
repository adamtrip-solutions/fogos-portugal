using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fogos.Worker.Jobs.Risk;

/// <summary>A concelho's identity from the polygon set: DICO code, display name, district.</summary>
public sealed record ConcelhoInfo(string Dico, string Concelho, string Distrito);

/// <summary>
/// The concelho polygon set for RCM GeoJSON assembly, loaded once from the embedded
/// <c>ConcelhoPolygons.geojson</c> resource (extracted verbatim from the legacy inline literal in
/// <c>ProcessRCM.php</c> — 278 mainland concelhos). Provides the concelho identity list for
/// <c>rcm_daily</c> rows and joins a horizon's per-DICO risk data onto the polygons for
/// <c>rcm_geojson</c> (mirrors <c>RCMTool::buildGeoJSON</c>: <c>feature.properties.data = local[dico].data</c>).
/// </summary>
public sealed class ConcelhoPolygons
{
    private const string ResourceSuffix = "Jobs.Risk.ConcelhoPolygons.geojson";
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    private static readonly Lazy<string> RawJson = new(LoadRaw, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<IReadOnlyList<ConcelhoInfo>> ConcelhoList =
        new(LoadConcelhos, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>All concelhos in the polygon set (DICO, title-cased name, district).</summary>
    public IReadOnlyList<ConcelhoInfo> Concelhos => ConcelhoList.Value;

    /// <summary>Title-cases a raw uppercase concelho name the way the legacy job did (pt-PT, e.g. ÁGUEDA → Águeda).</summary>
    public static string TitleCase(string raw) => Pt.TextInfo.ToTitleCase(raw.ToLower(Pt));

    /// <summary>
    /// Builds a horizon FeatureCollection, setting each feature's <c>properties.data</c> to the parsed
    /// <c>data</c> object for its DICO (or leaving it absent when the DICO has no risk entry). Returned
    /// as a JSON string — stored and served verbatim, never parsed back.
    /// </summary>
    public string BuildHorizonGeoJson(IReadOnlyDictionary<string, JsonElement> dataByDico)
    {
        var root = JsonNode.Parse(RawJson.Value)!.AsObject();
        var features = root["features"]!.AsArray();

        foreach (var feature in features)
        {
            var props = feature!["properties"]!.AsObject();
            var dico = props["DICO"]!.GetValue<string>();
            if (dataByDico.TryGetValue(dico, out var data))
                props["data"] = JsonNode.Parse(data.GetRawText());
        }

        return root.ToJsonString();
    }

    private static IReadOnlyList<ConcelhoInfo> LoadConcelhos()
    {
        using var doc = JsonDocument.Parse(RawJson.Value);
        var list = new List<ConcelhoInfo>();
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var props = feature.GetProperty("properties");
            list.Add(new ConcelhoInfo(
                props.GetProperty("DICO").GetString()!,
                TitleCase(props.GetProperty("Concelho").GetString()!),
                props.GetProperty("Distrito").GetString()!));
        }
        return list;
    }

    private static string LoadRaw()
    {
        var assembly = typeof(ConcelhoPolygons).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded concelho polygon resource '*{ResourceSuffix}' not found.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
