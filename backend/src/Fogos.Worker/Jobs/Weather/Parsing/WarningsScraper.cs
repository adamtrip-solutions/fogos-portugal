using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fogos.Domain.Time;

namespace Fogos.Worker.Jobs.Weather.Parsing;

/// <summary>One non-green IPMA awareness warning lifted from the homepage JS blob.</summary>
public sealed record ScrapedWarning(
    string AreaCode,
    string AwarenessType,
    string Level,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Text,
    string Control);

/// <summary>
/// Scrapes the IPMA homepage for the inline <c>var result_warnings = …;</c> JS object.
/// Faithful port of <c>HandleWeatherWarnings.php:54-67</c> including the magic offset arithmetic
/// (strip 3 trailing chars, trim, strip 1 more) and the <c>%uXXXX → &amp;#xXXXX;</c> replacement —
/// note legacy turns the escapes into HTML entities and json_decodes that, so entity sequences remain
/// literal in the stored text (matching the live platform). Green warnings are dropped; each retained
/// warning gets an md5 <c>control</c> hash over its raw JSON for idempotent ingest.
/// </summary>
public static class WarningsScraper
{
    private const string Marker = "var result_warnings = ";
    private const string SeaMarker = "//GET SEA DATA";

    public static IReadOnlyList<ScrapedWarning> Parse(string html, IClock clock)
    {
        var json = ExtractJson(html);
        using var doc = JsonDocument.Parse(json);

        var result = new List<ScrapedWarning>();
        if (IpmaJson.Prop(doc.RootElement, "data") is not { ValueKind: JsonValueKind.Array } data)
            return result;

        foreach (var d in data.EnumerateArray())
        {
            var level = IpmaJson.ReadString(IpmaJson.Prop(d, "awarenessLevelID"));
            if (level is null || string.Equals(level, "green", StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new ScrapedWarning(
                AreaCode: IpmaJson.ReadString(IpmaJson.Prop(d, "idAreaAviso")) ?? "",
                AwarenessType: IpmaJson.ReadString(IpmaJson.Prop(d, "awarenessTypeName")) ?? "",
                Level: level,
                StartsAt: IpmaJson.ParseLisbon(IpmaJson.ReadString(IpmaJson.Prop(d, "startTime")), clock) ?? default,
                EndsAt: IpmaJson.ParseLisbon(IpmaJson.ReadString(IpmaJson.Prop(d, "endTime")), clock) ?? default,
                Text: IpmaJson.ReadString(IpmaJson.Prop(d, "text")),
                Control: Md5(d.GetRawText())));
        }

        return result;
    }

    /// <summary>Extracts and cleans the raw JSON literal exactly as the legacy scraper did.</summary>
    private static string ExtractJson(string html)
    {
        var start = html.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0)
            throw new FormatException("IPMA homepage: 'var result_warnings' marker not found.");
        var after = html[(start + Marker.Length)..];

        var seaIdx = after.IndexOf(SeaMarker, StringComparison.Ordinal);
        if (seaIdx < 0)
            throw new FormatException("IPMA homepage: '//GET SEA DATA' terminator not found.");
        var body = after[..seaIdx];

        if (body.Length < 3)
            throw new FormatException("IPMA homepage: result_warnings block too short.");
        var trimmedTail = body[..^3];                                    // str_split drop last 3
        var entities = Regex.Replace(trimmedTail, "%u([0-9A-Fa-f]+)", "&#x$1;");
        var cleaned = entities.Trim();
        if (cleaned.Length < 1)
            throw new FormatException("IPMA homepage: result_warnings block empty after trim.");
        return cleaned[..^1];                                            // substr(...,0,-1)
    }

    private static string Md5(string raw)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(bytes);
    }
}
