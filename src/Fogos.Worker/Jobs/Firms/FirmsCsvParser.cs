using System.Globalization;
using Fogos.Domain.Geo;
using Fogos.Domain.Hotspots;

namespace Fogos.Worker.Jobs.Firms;

/// <summary>
/// Pure, fixture-testable parser for NASA FIRMS area-CSV responses (VIIRS or MODIS). Header-driven so
/// it tolerates the differing column sets: latitude/longitude → <see cref="GeoPoint"/>, acq_date +
/// acq_time (UTC) → an instant, brightness (MODIS <c>brightness</c> or VIIRS <c>bright_ti4</c>), and
/// confidence (numeric for MODIS, l/n/h for VIIRS) kept as a raw string. Header-only or blank bodies
/// yield an empty list without throwing.
/// </summary>
public static class FirmsCsvParser
{
    public static IReadOnlyList<HotspotSample> Parse(string csv)
    {
        var samples = new List<HotspotSample>();
        if (string.IsNullOrWhiteSpace(csv))
            return samples;

        var lines = csv.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return samples;

        var header = SplitCsv(lines[0]);
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
            col[header[i].Trim()] = i;

        for (var r = 1; r < lines.Length; r++)
        {
            var fields = SplitCsv(lines[r]);
            if (fields.Length < header.Length)
                continue;

            var lat = Field(fields, col, "latitude");
            var lng = Field(fields, col, "longitude");
            if (!TryDouble(lat, out var latVal) || !TryDouble(lng, out var lngVal))
                continue;
            if (latVal is < -90 or > 90 || lngVal is < -180 or > 180)
                continue;

            var brightnessRaw = Field(fields, col, "brightness") ?? Field(fields, col, "bright_ti4");
            var brightness = TryDouble(brightnessRaw, out var b) ? b : (double?)null;
            var confidence = Field(fields, col, "confidence");

            var acquiredAt = ParseAcquired(Field(fields, col, "acq_date"), Field(fields, col, "acq_time"));

            samples.Add(new HotspotSample(
                GeoPoint.FromLatLng(latVal, lngVal),
                acquiredAt,
                brightness,
                string.IsNullOrWhiteSpace(confidence) ? null : confidence));
        }

        return samples;
    }

    /// <summary>acq_date (yyyy-MM-dd) + acq_time (UTC HHmm, 1–4 digits) → a UTC instant; null if unparseable.</summary>
    internal static DateTimeOffset? ParseAcquired(string? date, string? time)
    {
        if (string.IsNullOrWhiteSpace(date)
            || !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return null;

        var hh = 0;
        var mm = 0;
        if (!string.IsNullOrWhiteSpace(time) && int.TryParse(time.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hhmm))
        {
            hh = hhmm / 100;
            mm = hhmm % 100;
        }
        if (hh is < 0 or > 23 || mm is < 0 or > 59)
            return null;

        return new DateTimeOffset(d.Year, d.Month, d.Day, hh, mm, 0, TimeSpan.Zero);
    }

    private static string? Field(string[] fields, IReadOnlyDictionary<string, int> col, string name) =>
        col.TryGetValue(name, out var i) && i < fields.Length ? fields[i] : null;

    private static bool TryDouble(string? s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // FIRMS CSV is simple comma-separated with no embedded commas in the fields we read, but handle
    // basic double-quoted fields defensively.
    private static string[] SplitCsv(string line)
    {
        if (line.IndexOf('"') < 0)
            return line.Split(',');

        var result = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (ch == ',' && !inQuotes) { result.Add(field.ToString()); field.Clear(); }
            else field.Append(ch);
        }
        result.Add(field.ToString());
        return result.ToArray();
    }
}
