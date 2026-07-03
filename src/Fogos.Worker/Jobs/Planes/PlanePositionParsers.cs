using System.Text.Json;
using Fogos.Domain.Aircraft;
using Fogos.Domain.Geo;

namespace Fogos.Worker.Jobs.Planes;

/// <summary>
/// A provider-agnostic parsed sample, before it becomes a <see cref="FlightPosition"/>. Kept as a
/// value type so the parsers stay pure and unit-testable without a database.
/// </summary>
public readonly record struct PlaneSample(
    string Icao,
    string? Registration,
    double Latitude,
    double Longitude,
    double? Altitude,
    DateTimeOffset? SampledAt,
    string? Fr24Id);

/// <summary>
/// Parses the Flightradar24 <c>live/flight-positions/light</c> response. Mirrors the legacy
/// <c>Fr24Tool::mapToFlightPosition</c> field mapping (<c>hex → icao</c>, <c>reg</c>, <c>lat/lon</c>,
/// <c>alt</c>, <c>timestamp</c>, <c>fr24_id</c>). Rows without a hex or a coordinate are dropped.
/// </summary>
public static class Fr24PositionParser
{
    public const string Source = "fr24";

    public static IReadOnlyList<PlaneSample> Parse(string json)
    {
        var samples = new List<PlaneSample>();
        if (string.IsNullOrWhiteSpace(json))
            return samples;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            return samples;

        foreach (var row in data.EnumerateArray())
        {
            var hex = GetString(row, "hex");
            if (string.IsNullOrWhiteSpace(hex))
                continue;

            var lat = GetDouble(row, "lat");
            var lon = GetDouble(row, "lon");
            if (lat is null || lon is null)
                continue;

            samples.Add(new PlaneSample(
                Icao: hex.ToLowerInvariant(),
                Registration: GetString(row, "reg"),
                Latitude: lat.Value,
                Longitude: lon.Value,
                Altitude: GetDouble(row, "alt"),
                SampledAt: ParseTimestamp(row),
                Fr24Id: GetString(row, "fr24_id")));
        }

        return samples;
    }

    private static DateTimeOffset? ParseTimestamp(JsonElement row)
    {
        if (!row.TryGetProperty("timestamp", out var ts))
            return null;

        // FR24 sends either an epoch-second integer or an ISO-8601 string.
        if (ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds(epoch);
        if (ts.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(ts.GetString(), out var parsed))
            return parsed.ToUniversalTime();
        return null;
    }

    private static string? GetString(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? GetDouble(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), out var d) => d,
            _ => null,
        };
    }
}

/// <summary>
/// Parses the adsb.fi / airplanes.live <c>/hex/{list}</c> response. Mirrors the legacy
/// <c>AdsbExchangeTool::mapToFlightPosition</c>: reads the <c>ac[]</c> array, prefers
/// <c>lastPosition.lat/lon</c> then falls back to top-level <c>lat/lon</c>, drops samples whose
/// <c>seen_pos</c> is older than <see cref="MaxSeenPositionSeconds"/>, and derives
/// <c>sampledAt = now − seen_pos</c>. Altitude is <c>alt_baro</c> when numeric ("ground" → null).
/// </summary>
public static class AdsbPositionParser
{
    /// <summary>Legacy <c>MAX_SEEN_POS_SECONDS</c> — positions staler than this are ignored.</summary>
    public const int MaxSeenPositionSeconds = 600;

    public static IReadOnlyList<PlaneSample> Parse(string json, DateTimeOffset now)
    {
        var samples = new List<PlaneSample>();
        if (string.IsNullOrWhiteSpace(json))
            return samples;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("ac", out var ac) ||
            ac.ValueKind != JsonValueKind.Array)
            return samples;

        foreach (var row in ac.EnumerateArray())
        {
            var hex = GetString(row, "hex");
            if (string.IsNullOrWhiteSpace(hex))
                continue;

            var lastPosition = row.TryGetProperty("lastPosition", out var lp) && lp.ValueKind == JsonValueKind.Object
                ? lp
                : (JsonElement?)null;

            var lat = GetDouble(lastPosition, "lat") ?? GetDouble(row, "lat");
            var lon = GetDouble(lastPosition, "lon") ?? GetDouble(row, "lon");
            if (lat is null || lon is null)
                continue;

            var seenPos = GetDouble(lastPosition, "seen_pos") ?? GetDouble(row, "seen_pos");
            if (seenPos is > MaxSeenPositionSeconds)
                continue;

            var sampledAt = seenPos is { } s ? now.AddSeconds(-s) : (DateTimeOffset?)null;

            samples.Add(new PlaneSample(
                Icao: hex.ToLowerInvariant(),
                Registration: GetString(row, "r"),
                Latitude: lat.Value,
                Longitude: lon.Value,
                Altitude: GetAltitude(row),
                SampledAt: sampledAt,
                Fr24Id: null));
        }

        return samples;
    }

    private static double? GetAltitude(JsonElement row)
    {
        if (!row.TryGetProperty("alt_baro", out var v))
            return null;
        return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null; // "ground" → null
    }

    private static string? GetString(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? GetDouble(JsonElement? element, string name)
    {
        if (element is not { } row || !row.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), out var d) => d,
            _ => null,
        };
    }
}
