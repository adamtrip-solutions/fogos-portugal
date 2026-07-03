namespace Fogos.Worker.Jobs.Planes;

/// <summary>
/// Pure sunrise/sunset math (NOAA "sunrise equation") plus the legacy daylight-polling window
/// (<c>sunrise + 1h → sunset − 1h</c>). The legacy FR24/ADSB jobs gated on PHP's
/// <c>date_sun_info()</c> for Lisbon; this reproduces the same window without any framework state,
/// so it is fully unit-testable. All returned instants are UTC.
/// </summary>
public static class SolarWindow
{
    /// <summary>Lisbon reference coordinates (legacy <c>LISBON_LAT/LON</c>).</summary>
    public const double LisbonLat = 38.7223;
    public const double LisbonLon = -9.1393;

    /// <summary>The legacy pre-sunrise / post-sunset trim applied to the polling window.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromHours(1);

    private const double Deg2Rad = Math.PI / 180.0;
    private const double Rad2Deg = 180.0 / Math.PI;

    /// <summary>
    /// Sunrise and sunset (UTC) for a location on a calendar date, or <c>null</c> during polar
    /// day/night (the sun never crosses the horizon). Uses the standard −0.833° geometric horizon.
    /// </summary>
    public static (DateTimeOffset Sunrise, DateTimeOffset Sunset)? SunriseSunset(double latitude, double longitude, DateOnly date)
    {
        // Mean-solar-noon term. With an east-positive longitude (Lisbon = −9.14), a westerly
        // location must push solar noon *later* in UTC, so the longitude enters J* directly.
        var lw = longitude;

        var midnightUtc = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
        var jDate = ToJulian(midnightUtc);

        var n = Math.Ceiling(jDate - 2451545.0 + 0.0008);
        var jStar = n - lw / 360.0;

        var m = Mod(357.5291 + 0.98560028 * jStar, 360.0);
        var mRad = m * Deg2Rad;
        var c = 1.9148 * Math.Sin(mRad) + 0.0200 * Math.Sin(2 * mRad) + 0.0003 * Math.Sin(3 * mRad);
        var lambda = Mod(m + c + 180.0 + 102.9372, 360.0);
        var lambdaRad = lambda * Deg2Rad;

        var jTransit = 2451545.0 + jStar + 0.0053 * Math.Sin(mRad) - 0.0069 * Math.Sin(2 * lambdaRad);

        var sinDecl = Math.Sin(lambdaRad) * Math.Sin(23.44 * Deg2Rad);
        var cosDecl = Math.Cos(Math.Asin(sinDecl));

        var latRad = latitude * Deg2Rad;
        var cosOmega = (Math.Sin(-0.833 * Deg2Rad) - Math.Sin(latRad) * sinDecl) / (Math.Cos(latRad) * cosDecl);
        if (cosOmega is > 1.0 or < -1.0)
            return null; // polar day or night — the horizon is never crossed

        var omega = Math.Acos(cosOmega) * Rad2Deg;
        var sunrise = FromJulian(jTransit - omega / 360.0);
        var sunset = FromJulian(jTransit + omega / 360.0);
        return (sunrise, sunset);
    }

    /// <summary>The daylight polling window <c>[sunrise + Margin, sunset − Margin]</c> (UTC), or null on polar days.</summary>
    public static (DateTimeOffset Open, DateTimeOffset Close)? PollingWindow(double latitude, double longitude, DateOnly date)
    {
        if (SunriseSunset(latitude, longitude, date) is not { } sun)
            return null;
        return (sun.Sunrise + Margin, sun.Sunset - Margin);
    }

    /// <summary>True when <paramref name="instantUtc"/> falls inside that day's polling window.</summary>
    public static bool IsWithinDaylight(double latitude, double longitude, DateTimeOffset instantUtc)
    {
        var utc = instantUtc.ToUniversalTime();
        if (PollingWindow(latitude, longitude, DateOnly.FromDateTime(utc.UtcDateTime)) is not { } window)
            return false;
        return utc >= window.Open && utc <= window.Close;
    }

    /// <summary>Convenience overload pinned to Lisbon (the only location the plane jobs poll for).</summary>
    public static bool IsLisbonDaylight(DateTimeOffset instantUtc) =>
        IsWithinDaylight(LisbonLat, LisbonLon, instantUtc);

    private static double ToJulian(DateTime utc) => utc.ToOADate() + 2415018.5;

    private static DateTimeOffset FromJulian(double julian) =>
        new(DateTime.SpecifyKind(DateTime.FromOADate(julian - 2415018.5), DateTimeKind.Utc));

    private static double Mod(double value, double modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}
