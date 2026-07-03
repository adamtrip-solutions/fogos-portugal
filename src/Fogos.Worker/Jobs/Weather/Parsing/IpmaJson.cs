using System.Globalization;
using System.Text.Json;
using Fogos.Domain.Time;

namespace Fogos.Worker.Jobs.Weather.Parsing;

/// <summary>
/// Shared JSON-reading helpers for the IPMA feeds, including the <c>-99 → null</c> sentinel filter
/// (IPMA's "no data" marker; legacy <c>UpdateWeatherData.php:85-92</c>) and lenient number reading
/// (IPMA occasionally serialises numeric metrics as strings).
/// </summary>
internal static class IpmaJson
{
    /// <summary>IPMA "no data" sentinel. Any metric equal to this becomes null.</summary>
    public const double NoData = -99.0;

    public static JsonElement? Prop(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) ? v : null;

    /// <summary>Read a number that may be encoded as a JSON number or a numeric string; null otherwise.</summary>
    public static double? ReadNumber(JsonElement? el)
    {
        if (el is not { } e)
            return null;
        return e.ValueKind switch
        {
            JsonValueKind.Number => e.TryGetDouble(out var d) ? d : null,
            JsonValueKind.String => double.TryParse(e.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => null,
        };
    }

    public static int? ReadInt(JsonElement? el)
    {
        var n = ReadNumber(el);
        return n is null ? null : (int)Math.Round(n.Value);
    }

    public static string? ReadString(JsonElement? el) =>
        el is { ValueKind: JsonValueKind.String } e ? e.GetString() : null;

    /// <summary>A metric: read the number, then drop the -99 sentinel and nulls to null.</summary>
    public static double? ReadMetric(JsonElement obj, string name) => Sentinel(ReadNumber(Prop(obj, name)));

    /// <summary>Applies the -99 → null rule (loose equality, matching legacy <c>$value == -99</c>).</summary>
    public static double? Sentinel(double? value) =>
        value is null || Math.Abs(value.Value - NoData) < 0.0001 ? null : value;

    /// <summary>IPMA observation timestamps are UTC (legacy <c>Carbon::parse($date, 'UTC')</c>).</summary>
    public static DateTimeOffset? ParseUtc(string? s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto
            : null;

    /// <summary>Parse a naive IPMA date key to a calendar date.</summary>
    public static DateOnly? ParseDate(string? s)
    {
        if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }

    /// <summary>IPMA warning times are Lisbon-local naive strings; interpret as Lisbon → UTC.</summary>
    public static DateTimeOffset? ParseLisbon(string? s, IClock clock) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? clock.FromLisbon(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified))
            : null;
}
