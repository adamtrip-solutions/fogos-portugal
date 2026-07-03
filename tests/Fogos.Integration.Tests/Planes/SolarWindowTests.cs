using Fogos.Worker.Jobs.Planes;

namespace Fogos.Integration.Tests.Planes;

/// <summary>Pure sunrise/sunset math — no containers. Tolerance ±15 min vs published Lisbon values.</summary>
public sealed class SolarWindowTests
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(15);

    [Fact]
    public void Lisbon_summer_solstice_sunrise_and_sunset_are_plausible()
    {
        var sun = SolarWindow.SunriseSunset(SolarWindow.LisbonLat, SolarWindow.LisbonLon, new DateOnly(2026, 6, 21));
        Assert.NotNull(sun);

        // 2026-06-21 Lisbon: sunrise ≈ 05:11 UTC, sunset ≈ 20:05 UTC (06:11 / 21:05 local DST).
        AssertNear(new DateTimeOffset(2026, 6, 21, 5, 11, 0, TimeSpan.Zero), sun!.Value.Sunrise);
        AssertNear(new DateTimeOffset(2026, 6, 21, 20, 5, 0, TimeSpan.Zero), sun.Value.Sunset);
    }

    [Fact]
    public void Lisbon_winter_solstice_sunrise_and_sunset_are_plausible()
    {
        var sun = SolarWindow.SunriseSunset(SolarWindow.LisbonLat, SolarWindow.LisbonLon, new DateOnly(2026, 12, 21));
        Assert.NotNull(sun);

        // 2026-12-21 Lisbon (WET, no DST): sunrise ≈ 07:52 UTC, sunset ≈ 17:20 UTC.
        AssertNear(new DateTimeOffset(2026, 12, 21, 7, 52, 0, TimeSpan.Zero), sun!.Value.Sunrise);
        AssertNear(new DateTimeOffset(2026, 12, 21, 17, 20, 0, TimeSpan.Zero), sun.Value.Sunset);
    }

    [Fact]
    public void Polling_window_trims_one_hour_from_each_end()
    {
        var sun = SolarWindow.SunriseSunset(SolarWindow.LisbonLat, SolarWindow.LisbonLon, new DateOnly(2026, 6, 21))!.Value;
        var window = SolarWindow.PollingWindow(SolarWindow.LisbonLat, SolarWindow.LisbonLon, new DateOnly(2026, 6, 21))!.Value;

        Assert.Equal(sun.Sunrise + TimeSpan.FromHours(1), window.Open);
        Assert.Equal(sun.Sunset - TimeSpan.FromHours(1), window.Close);
    }

    [Fact]
    public void Midday_is_daylight_and_predawn_is_not()
    {
        Assert.True(SolarWindow.IsLisbonDaylight(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)));
        Assert.False(SolarWindow.IsLisbonDaylight(new DateTimeOffset(2026, 6, 15, 2, 0, 0, TimeSpan.Zero)));
        Assert.False(SolarWindow.IsLisbonDaylight(new DateTimeOffset(2026, 12, 21, 23, 30, 0, TimeSpan.Zero)));
    }

    private static void AssertNear(DateTimeOffset expected, DateTimeOffset actual)
    {
        var delta = (actual - expected).Duration();
        Assert.True(delta <= Tolerance, $"expected {expected:HH:mm} ± {Tolerance.TotalMinutes:0}m, got {actual:HH:mm} (Δ {delta.TotalMinutes:0.0}m)");
    }
}
