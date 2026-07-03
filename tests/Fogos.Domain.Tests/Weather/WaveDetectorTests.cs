using Fogos.Domain.Weather;

namespace Fogos.Domain.Tests.Weather;

public class WaveDetectorTests
{
    private const int Station = 100;

    private static DailyWeather Day(DateOnly date, double? tempMax = null, double? tempMin = null) => new()
    {
        StationId = Station,
        Date = date,
        TempMax = tempMax,
        TempMin = tempMin,
    };

    /// <summary>Flat 12-month normal array (same value every month) unless overridden.</summary>
    private static double[] FlatNormals(double value) => Enumerable.Repeat(value, 12).ToArray();

    [Fact]
    public void Heat_six_consecutive_extreme_days_ending_today_is_an_ongoing_wave()
    {
        var today = new DateOnly(2026, 7, 15); // July, monthIdx 6
        var normals = FlatNormals(25.0);
        var daily = Enumerable.Range(0, 6)
            .Select(i => Day(today.AddDays(-5 + i), tempMax: 31.0)) // delta +6 > +5
            .ToList();

        var window = WaveDetector.Detect(WaveType.Heat, daily, normals, today);

        Assert.NotNull(window);
        Assert.Equal(today.AddDays(-5), window!.Start);
        Assert.Equal(today, window.End);
        Assert.True(window.Ongoing);
        Assert.Equal(6, window.Days.Count);
        Assert.Equal(6.0, window.PeakDeviation, 3);
        Assert.Equal(25.0, window.MonthNormal, 3);
        Assert.All(window.Days, d => Assert.Equal(6.0, d.Deviation, 3));
    }

    [Fact]
    public void Only_five_consecutive_extreme_days_is_no_wave()
    {
        var today = new DateOnly(2026, 7, 15);
        var normals = FlatNormals(25.0);
        // Five extreme days (today-4..today); the sixth (today-5) is normal, so no 6-run exists.
        var daily = new List<DailyWeather> { Day(today.AddDays(-5), tempMax: 24.0) };
        daily.AddRange(Enumerable.Range(0, 5).Select(i => Day(today.AddDays(-4 + i), tempMax: 31.0)));

        var window = WaveDetector.Detect(WaveType.Heat, daily, normals, today);

        Assert.Null(window);
    }

    [Fact]
    public void Window_spanning_a_month_boundary_mixes_per_day_normals()
    {
        var today = new DateOnly(2026, 7, 3);
        // June normal (idx 5) = 20, July normal (idx 6) = 25; all other months high so only this window qualifies.
        var normals = FlatNormals(100.0);
        normals[5] = 20.0; // June
        normals[6] = 25.0; // July

        var daily = new List<DailyWeather>
        {
            Day(new DateOnly(2026, 6, 28), tempMax: 27.0), // +7 vs June 20
            Day(new DateOnly(2026, 6, 29), tempMax: 27.0),
            Day(new DateOnly(2026, 6, 30), tempMax: 27.0),
            Day(new DateOnly(2026, 7, 1), tempMax: 31.0),  // +6 vs July 25
            Day(new DateOnly(2026, 7, 2), tempMax: 31.0),
            Day(new DateOnly(2026, 7, 3), tempMax: 31.0),
        };

        var window = WaveDetector.Detect(WaveType.Heat, daily, normals, today);

        Assert.NotNull(window);
        Assert.Equal(new DateOnly(2026, 6, 28), window!.Start);
        Assert.Equal(new DateOnly(2026, 7, 3), window.End);
        Assert.Equal(20.0, window.Days[0].Normal, 3);  // June day uses June normal
        Assert.Equal(25.0, window.Days[^1].Normal, 3); // July day uses July normal
        Assert.Equal(25.0, window.MonthNormal, 3);     // stored normal = last day's month
        Assert.Equal(7.0, window.PeakDeviation, 3);    // June days peaked at +7
    }

    [Fact]
    public void Cold_wave_detected_against_min_temperature_normal()
    {
        var today = new DateOnly(2026, 1, 20); // January, monthIdx 0
        var normals = FlatNormals(10.0);
        var daily = Enumerable.Range(0, 6)
            .Select(i => Day(today.AddDays(-5 + i), tempMin: 4.0)) // delta -6 < -5
            .ToList();

        var window = WaveDetector.Detect(WaveType.Cold, daily, normals, today);

        Assert.NotNull(window);
        Assert.Equal(today, window!.End);
        Assert.True(window.Ongoing);
        Assert.Equal(-6.0, window.PeakDeviation, 3);
    }

    [Fact]
    public void Cold_ignores_max_temperature_and_heat_ignores_min()
    {
        var today = new DateOnly(2026, 1, 20);
        var normals = FlatNormals(10.0);
        // Extreme max but benign min → no cold wave.
        var daily = Enumerable.Range(0, 6)
            .Select(i => Day(today.AddDays(-5 + i), tempMax: 40.0, tempMin: 10.0))
            .ToList();

        Assert.Null(WaveDetector.Detect(WaveType.Cold, daily, normals, today));
    }

    [Fact]
    public void When_several_windows_qualify_the_latest_wins()
    {
        var today = new DateOnly(2026, 7, 15);
        var normals = FlatNormals(25.0);
        // Nine consecutive extreme days (today-8..today) → four 6-day windows; latest ends today.
        var daily = Enumerable.Range(0, 9)
            .Select(i => Day(today.AddDays(-8 + i), tempMax: 31.0))
            .ToList();

        var window = WaveDetector.Detect(WaveType.Heat, daily, normals, today);

        Assert.NotNull(window);
        Assert.Equal(today.AddDays(-5), window!.Start);
        Assert.Equal(today, window.End);
        Assert.True(window.Ongoing);
    }

    [Fact]
    public void Window_ending_yesterday_is_ongoing()
    {
        var today = new DateOnly(2026, 7, 15);
        var normals = FlatNormals(25.0);
        // Six extreme days ending yesterday; today has no reading so the today-ending window breaks.
        var daily = Enumerable.Range(0, 6)
            .Select(i => Day(today.AddDays(-6 + i), tempMax: 31.0))
            .ToList();

        var window = WaveDetector.Detect(WaveType.Heat, daily, normals, today);

        Assert.NotNull(window);
        Assert.Equal(today.AddDays(-1), window!.End);
        Assert.True(window.Ongoing);
    }

    [Fact]
    public void Window_ending_two_days_ago_is_not_ongoing()
    {
        var today = new DateOnly(2026, 7, 15);
        var normals = FlatNormals(25.0);
        // Six extreme days ending two days ago; the two most recent days are missing.
        var daily = Enumerable.Range(0, 6)
            .Select(i => Day(today.AddDays(-7 + i), tempMax: 31.0))
            .ToList();

        var window = WaveDetector.Detect(WaveType.Heat, daily, normals, today);

        Assert.NotNull(window);
        Assert.Equal(today.AddDays(-2), window!.End);
        Assert.False(window.Ongoing);
    }

    [Fact]
    public void A_null_daily_value_breaks_the_streak()
    {
        var today = new DateOnly(2026, 7, 15);
        var normals = FlatNormals(25.0);
        var daily = Enumerable.Range(0, 6)
            .Select(i => Day(today.AddDays(-5 + i), tempMax: 31.0))
            .ToList();
        // Punch a null into the middle of the only candidate window.
        daily[3] = Day(today.AddDays(-2), tempMax: null);

        Assert.Null(WaveDetector.Detect(WaveType.Heat, daily, normals, today));
    }

    [Fact]
    public void A_missing_day_breaks_the_streak()
    {
        var today = new DateOnly(2026, 7, 15);
        var normals = FlatNormals(25.0);
        // Only five of the six days present (today-2 absent entirely).
        var daily = Enumerable.Range(0, 6)
            .Where(i => i != 3)
            .Select(i => Day(today.AddDays(-5 + i), tempMax: 31.0))
            .ToList();

        Assert.Null(WaveDetector.Detect(WaveType.Heat, daily, normals, today));
    }
}
