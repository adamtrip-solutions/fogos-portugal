using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fogos.Domain.Risk;

namespace Fogos.Worker.Jobs.Risk;

/// <summary>Why an RCM page failed to parse — drives the ops alert wording and job outcome.</summary>
public enum RcmParseFailure
{
    /// <summary>The <c>rcmF[]</c> assignments or their JSON are gone/changed — the IPMA page shape moved.</summary>
    PageShapeChanged,

    /// <summary>The page parsed but a horizon carried no per-concelho risk map (empty run).</summary>
    EmptyData,
}

/// <summary>Raised by <see cref="RcmPageParser"/> with a precise <see cref="Failure"/> classification.</summary>
public sealed class RcmParseException(RcmParseFailure failure, string message) : Exception(message)
{
    public RcmParseFailure Failure { get; } = failure;
}

/// <summary>
/// One forecast horizon extracted from a single <c>rcmF[i]</c> assignment: the per-DICO risk map plus
/// the run metadata. <see cref="Data"/> holds the verbatim <c>data</c> object per DICO (contains the
/// <c>rcm</c> level 1–5) so it can be both read as a level and re-embedded into the horizon GeoJSON.
/// </summary>
public sealed class RcmHorizon
{
    public required int Index { get; init; }
    public required string WhenKey { get; init; }
    public required DateOnly ForecastDate { get; init; }
    public DateTimeOffset? RunAt { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> Data { get; init; }

    /// <summary>Risk level 1–5 for a DICO, or null when the DICO is absent / the level is missing.</summary>
    public int? Level(string dico) =>
        Data.TryGetValue(dico, out var data)
        && data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("rcm", out var rcm)
        && rcm.TryGetInt32(out var level)
            ? level
            : null;
}

/// <summary>All five horizons (hoje/amanha/depois/depois2/depois3) from one page load.</summary>
public sealed class RcmParseResult
{
    public required DateOnly ForecastDate { get; init; }
    public DateTimeOffset? RunAt { get; init; }

    /// <summary>Indexed 0..4 = today, tomorrow, after, after2, after3 (as available).</summary>
    public required IReadOnlyList<RcmHorizon> Horizons { get; init; }

    public RcmHorizon? Horizon(int index) => index >= 0 && index < Horizons.Count ? Horizons[index] : null;
}

/// <summary>
/// Pure, fixture-testable parser for the IPMA <c>riscoincendio/rcm.pt/index.jsp</c> page. Mirrors the
/// legacy <c>ProcessRCM.php</c> regex: strip line breaks, then pull each <c>rcmF[i] = {...};</c> JSON
/// literal. The single most brittle parser in the system, so failure is explicit: a moved page shape
/// and an empty-but-well-formed run are distinct <see cref="RcmParseFailure"/> outcomes.
/// </summary>
public static class RcmPageParser
{
    // The five horizons IPMA publishes, in order. Only 0..2 are strictly required (they back the served
    // GeoJSON horizons); 3 and 4 are best-effort so a shorter run doesn't fail the whole ingest.
    private static readonly string[] WhenKeys = ["hoje", "amanha", "depois", "depois2", "depois3"];
    private const int RequiredHorizons = 3;

    public static RcmParseResult Parse(string page)
    {
        if (string.IsNullOrWhiteSpace(page))
            throw new RcmParseException(RcmParseFailure.PageShapeChanged, "RCM page was empty.");

        // Legacy removed PHP_EOL; strip both CR and LF so the non-greedy `(.*?);` stays on one logical line.
        var flat = page.Replace("\r", "").Replace("\n", "");

        var horizons = new List<RcmHorizon>();
        for (var i = 0; i < WhenKeys.Length; i++)
        {
            var match = Regex.Match(flat, $@"rcmF\[{i}\]\s*=\s*(.*?);", RegexOptions.Singleline);
            if (!match.Success)
            {
                if (i < RequiredHorizons)
                    throw new RcmParseException(
                        RcmParseFailure.PageShapeChanged,
                        $"rcmF[{i}] ({WhenKeys[i]}) assignment not found — IPMA page shape changed.");
                break; // optional horizon absent: stop, keep what we have.
            }

            horizons.Add(ParseHorizon(i, WhenKeys[i], match.Groups[1].Value));
        }

        return new RcmParseResult
        {
            ForecastDate = horizons[0].ForecastDate,
            RunAt = horizons[0].RunAt,
            Horizons = horizons,
        };
    }

    private static RcmHorizon ParseHorizon(int index, string whenKey, string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new RcmParseException(
                RcmParseFailure.PageShapeChanged,
                $"rcmF[{index}] ({whenKey}) was not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("local", out var local) || local.ValueKind != JsonValueKind.Object)
                throw new RcmParseException(
                    RcmParseFailure.PageShapeChanged,
                    $"rcmF[{index}] ({whenKey}) has no 'local' object.");

            var data = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var entry in local.EnumerateObject())
            {
                if (entry.Value.TryGetProperty("data", out var d))
                    // Clone so the element survives the JsonDocument being disposed.
                    data[entry.Name] = d.Clone();
            }

            if (data.Count == 0)
                throw new RcmParseException(
                    RcmParseFailure.EmptyData,
                    $"rcmF[{index}] ({whenKey}) carried no concelho risk entries.");

            return new RcmHorizon
            {
                Index = index,
                WhenKey = whenKey,
                ForecastDate = ReadDate(root, "dataPrev"),
                RunAt = ReadRunAt(root, "dataRun"),
                Data = data,
            };
        }
    }

    private static DateOnly ReadDate(JsonElement root, string prop)
    {
        if (root.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
            if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return date;
        }
        throw new RcmParseException(RcmParseFailure.PageShapeChanged, $"RCM horizon missing/invalid '{prop}'.");
    }

    private static DateTimeOffset? ReadRunAt(JsonElement root, string prop)
    {
        if (root.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(el.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var run))
            return run;
        return null;
    }
}
