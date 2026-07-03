namespace Fogos.Domain.Weather;

/// <summary>
/// One detected qualifying window: 6+ consecutive days beyond the WMO threshold, the extreme-most
/// deviation across the window, the (last day's) month normal, and whether it still touches
/// today/yesterday. <see cref="Days"/> preserves the per-day observed/normal/deviation triples.
/// </summary>
public sealed record WaveWindow(
    DateOnly Start,
    DateOnly End,
    IReadOnlyList<WaveDay> Days,
    double PeakDeviation,
    double MonthNormal,
    bool Ongoing);

/// <summary>
/// Pure port of the legacy <c>DetectTemperatureWaves::evaluate</c> (DetectTemperatureWaves.php:70-163).
///
/// WMO rule: a wave is a run of at least <see cref="WindowDays"/> (6) consecutive days where the daily
/// extreme beats the month normal by more than the threshold — heat when
/// <c>tempMax − monthlyTmax &gt; +5</c> (vs the 1991-2020 period), cold when
/// <c>tempMin − monthlyTmin &lt; −5</c> (vs 1971-2000). A <see cref="LookbackDays"/> (10) window feeds
/// detection; each day is compared against ITS OWN month's normal, so a window crossing a month
/// boundary mixes two months' normals (preserved deliberately). When several qualifying windows exist,
/// the LATEST (largest start) wins. A window is <c>ongoing</c> iff it ends today or yesterday. Any
/// missing / null daily value (or a missing month normal) breaks the streak.
///
/// Side effects (persistence, resetting prior ongoing flags, ops notifications) live in the job — this
/// function is deterministic given (type, daily, normals, today).
/// </summary>
public static class WaveDetector
{
    public const int WindowDays = 6;
    public const double HeatDelta = 5.0;
    public const double ColdDelta = -5.0;
    public const int LookbackDays = 10;

    /// <param name="type">Heat compares daily max vs the tmax normal; cold compares daily min vs tmin.</param>
    /// <param name="daily">Daily records (any order); indexed internally by date.</param>
    /// <param name="monthlyNormals">12 monthly normals (index 0 = January) for the chosen type.</param>
    /// <param name="today">"Today" in Lisbon-local terms — the right edge of the lookback window.</param>
    /// <returns>The latest qualifying window, or null when none exists.</returns>
    public static WaveWindow? Detect(
        WaveType type,
        IReadOnlyList<DailyWeather> daily,
        IReadOnlyList<double> monthlyNormals,
        DateOnly today)
    {
        // Latest reading per date wins if duplicates are present.
        var byDate = new Dictionary<DateOnly, DailyWeather>();
        foreach (var d in daily)
            byDate[d.Date] = d;

        var start = today.AddDays(-LookbackDays);
        var lastStart = today.AddDays(-(WindowDays - 1)); // cursor <= today - 5

        WaveWindow? best = null;

        for (var cursor = start; cursor <= lastStart; cursor = cursor.AddDays(1))
        {
            var days = new List<WaveDay>(WindowDays);
            var broken = false;
            var peak = type == WaveType.Heat ? double.NegativeInfinity : double.PositiveInfinity;

            for (var i = 0; i < WindowDays; i++)
            {
                var day = cursor.AddDays(i);

                if (!byDate.TryGetValue(day, out var row))
                {
                    broken = true;
                    break;
                }

                var value = type == WaveType.Heat ? row.TempMax : row.TempMin;
                if (value is null)
                {
                    broken = true;
                    break;
                }

                var monthIdx = day.Month - 1;
                if (monthIdx < 0 || monthIdx >= monthlyNormals.Count)
                {
                    broken = true;
                    break;
                }

                var monthNormal = monthlyNormals[monthIdx];
                var delta = value.Value - monthNormal;
                var extreme = type == WaveType.Heat ? delta > HeatDelta : delta < ColdDelta;
                if (!extreme)
                {
                    broken = true;
                    break;
                }

                peak = type == WaveType.Heat ? Math.Max(peak, delta) : Math.Min(peak, delta);
                days.Add(new WaveDay(day, value.Value, monthNormal, Round(delta)));
            }

            if (broken || days.Count != WindowDays)
                continue;

            var end = cursor.AddDays(WindowDays - 1);
            var ongoing = end == today || end == today.AddDays(-1);
            // Overwrite: a later cursor produces a later window, so the LATEST qualifying window wins.
            best = new WaveWindow(cursor, end, days, Round(peak), days[^1].Normal, ongoing);
        }

        return best;
    }

    private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
