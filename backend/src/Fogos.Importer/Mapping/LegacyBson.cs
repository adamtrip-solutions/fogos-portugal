using System.Globalization;
using Fogos.Domain.Geo;
using Fogos.Domain.Time;
using MongoDB.Bson;

namespace Fogos.Importer.Mapping;

/// <summary>
/// Shared readers that absorb the legacy dirt once, at import time: multi-shaped dates
/// (<c>{sec:N}</c> / BSON date / <c>d-m-Y H:i</c> Lisbon strings), double-or-int-or-string
/// numerics, the <c>-99</c> "no data" sentinel, DICO padding, and coordinate recovery.
/// </summary>
public static class LegacyBson
{
    /// <summary>Portugal (incl. Azores/Madeira) plausibility box, used to recover swapped incident coordinates.</summary>
    private const double PtLatMin = 29, PtLatMax = 43, PtLngMin = -32, PtLngMax = -6;

    /// <summary>Returns the value for <paramref name="name"/>, or null when absent or BSON null.</summary>
    public static BsonValue? Get(BsonDocument doc, string name) =>
        doc.TryGetValue(name, out var v) && !v.IsBsonNull ? v : null;

    /// <summary>First present, non-null value among <paramref name="names"/>.</summary>
    public static BsonValue? GetAny(BsonDocument doc, params string[] names)
    {
        foreach (var name in names)
            if (Get(doc, name) is { } v)
                return v;
        return null;
    }

    public static string? ReadString(BsonValue? v)
    {
        if (v is null) return null;
        var s = v.BsonType switch
        {
            BsonType.String => v.AsString,
            BsonType.Boolean => null, // legacy `false` sentinel meaning "none" (regiao/sub_regiao)
            _ => v.ToString(),
        };
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public static double? ReadDouble(BsonValue? v)
    {
        switch (v?.BsonType)
        {
            case null:
            case BsonType.Null:
                return null;
            case BsonType.Double:
                return v!.AsDouble;
            case BsonType.Int32:
                return v!.AsInt32;
            case BsonType.Int64:
                return v!.AsInt64;
            case BsonType.Decimal128:
                return (double)v!.AsDecimal128;
            case BsonType.String:
                var s = v!.AsString.Trim().Replace('−', '-'); // unicode minus → ascii
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
            default:
                return null;
        }
    }

    public static int? ReadInt(BsonValue? v)
    {
        var d = ReadDouble(v);
        return d is { } x ? (int)Math.Round(x) : null;
    }

    public static bool ReadBool(BsonValue? v, bool fallback = false)
    {
        return v?.BsonType switch
        {
            null or BsonType.Null => fallback,
            BsonType.Boolean => v!.AsBoolean,
            BsonType.Int32 => v!.AsInt32 != 0,
            BsonType.Int64 => v!.AsInt64 != 0,
            BsonType.Double => v!.AsDouble != 0,
            BsonType.String => v!.AsString.Trim().ToLowerInvariant() is "1" or "true" or "yes",
            _ => fallback,
        };
    }

    /// <summary>IPMA's -99 / -99.0 / "−99" no-data marker → null. Apply after <see cref="ReadDouble"/>.</summary>
    public static double? Sentinel(double? v) => v is { } x && Math.Abs(x - (-99)) < 0.0001 ? null : v;

    public static double? ReadMetric(BsonValue? v) => Sentinel(ReadDouble(v));

    /// <summary>Zero-pad a DICO code to 4 chars ("111" → "0111", "00" preserved). Null/empty → "".</summary>
    public static string PadDico(BsonValue? v)
    {
        var s = ReadString(v);
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        return s.Length >= 4 ? s : s.PadLeft(4, '0');
    }

    private static readonly string[] LisbonFormats =
    [
        "d-M-yyyy H:mm", "d-M-yyyy HH:mm", "dd-MM-yyyy HH:mm", "dd-MM-yyyy H:mm",
        "d-M-yyyy", "dd-MM-yyyy",
    ];

    /// <summary>
    /// Reads any legacy timestamp shape into a UTC instant. BSON dates are already UTC;
    /// <c>{sec:N}</c> is unix seconds; naive <c>d-m-Y[ H:i]</c> strings are Europe/Lisbon.
    /// </summary>
    public static DateTimeOffset? ReadDate(BsonValue? v, IClock clock)
    {
        switch (v?.BsonType)
        {
            case null:
            case BsonType.Null:
                return null;
            case BsonType.DateTime:
                return new DateTimeOffset(v!.ToUniversalTime());
            case BsonType.Document:
                var doc = v!.AsBsonDocument;
                if (Get(doc, "sec") is { } sec && ReadDouble(sec) is { } secs)
                    return DateTimeOffset.FromUnixTimeMilliseconds((long)(secs * 1000));
                // $date extended-json shapes usually parse as BsonType.DateTime already.
                return null;
            case BsonType.Int64:
            case BsonType.Int32:
            case BsonType.Double:
                var n = ReadDouble(v)!.Value;
                // Heuristic: milliseconds if it looks like a modern ms epoch, else seconds.
                return n > 3_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)n)
                    : DateTimeOffset.FromUnixTimeSeconds((long)n);
            case BsonType.String:
                var s = v!.AsString.Trim();
                if (string.IsNullOrEmpty(s)) return null;
                if (DateTime.TryParseExact(s, LisbonFormats, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var naive))
                    return clock.FromLisbon(naive);
                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var iso))
                    return iso;
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Builds a GeoPoint from a (latitude, longitude) candidate pair, recovering the legacy
    /// [lat,lng]/[lng,lat] inversion trap: prefers a Portugal-plausible reading, swaps when the
    /// swapped pair is the plausible one, and returns null when neither is even in valid range.
    /// </summary>
    public static GeoPoint? MakePoint(double? latCandidate, double? lngCandidate)
    {
        if (latCandidate is not { } a || lngCandidate is not { } b)
            return null;
        if (a == 0 && b == 0)
            return null;

        static bool Plausible(double lat, double lng) =>
            lat is >= PtLatMin and <= PtLatMax && lng is >= PtLngMin and <= PtLngMax;
        static bool InRange(double lat, double lng) =>
            lat is >= -90 and <= 90 && lng is >= -180 and <= 180;

        if (Plausible(a, b)) return GeoPoint.FromLatLng(a, b);
        if (Plausible(b, a)) return GeoPoint.FromLatLng(b, a); // swapped
        if (InRange(a, b)) return GeoPoint.FromLatLng(a, b);
        if (InRange(b, a)) return GeoPoint.FromLatLng(b, a);
        return null;
    }

    /// <summary>Reads a <c>[a, b]</c> numeric pair, or null.</summary>
    public static (double A, double B)? ReadPair(BsonValue? v)
    {
        if (v is not { BsonType: BsonType.Array } || v.AsBsonArray.Count < 2)
            return null;
        var arr = v.AsBsonArray;
        if (ReadDouble(arr[0]) is { } a && ReadDouble(arr[1]) is { } b)
            return (a, b);
        return null;
    }

    /// <summary>
    /// Carries the legacy ObjectId <c>_id</c> forward as the new surrogate <c>_id</c> so re-imports
    /// replace the same document (idempotent). Falls back to a fresh ObjectId when the legacy
    /// <c>_id</c> is absent or not an ObjectId.
    /// </summary>
    public static string CarryObjectId(BsonDocument doc) =>
        doc.TryGetValue("_id", out var id) && id.BsonType == BsonType.ObjectId
            ? id.AsObjectId.ToString()
            : ObjectId.GenerateNewId().ToString();
}
