namespace Fogos.Domain.Geo;

/// <summary>
/// The single coordinate type of the system. The legacy platform mixed [lat,lng] and
/// [lng,lat] arrays; here construction is only possible through named factories and
/// storage is always GeoJSON ([lng,lat]) at the infrastructure edge.
/// </summary>
public readonly record struct GeoPoint
{
    public double Latitude { get; }
    public double Longitude { get; }

    private GeoPoint(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be within [-90, 90].");
        if (longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be within [-180, 180].");
        Latitude = latitude;
        Longitude = longitude;
    }

    public static GeoPoint FromLatLng(double latitude, double longitude) => new(latitude, longitude);

    /// <summary>GeoJSON coordinate order: [longitude, latitude].</summary>
    public static GeoPoint FromGeoJson(double longitude, double latitude) => new(latitude, longitude);

    public static GeoPoint FromGeoJson(IReadOnlyList<double> lngLat) =>
        lngLat.Count == 2
            ? new GeoPoint(lngLat[1], lngLat[0])
            : throw new ArgumentException("GeoJSON coordinates must be [lng, lat].", nameof(lngLat));

    /// <summary>Great-circle distance in kilometers (haversine, R = 6371 — matches the legacy math).</summary>
    public double DistanceKm(GeoPoint other)
    {
        const double earthRadiusKm = 6371;
        var dLat = ToRadians(other.Latitude - Latitude);
        var dLng = ToRadians(other.Longitude - Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(Latitude)) * Math.Cos(ToRadians(other.Latitude))
                * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
