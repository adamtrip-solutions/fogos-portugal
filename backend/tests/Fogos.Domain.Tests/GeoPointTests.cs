using Fogos.Domain.Geo;

namespace Fogos.Domain.Tests;

public class GeoPointTests
{
    [Fact]
    public void FromLatLng_and_FromGeoJson_agree_on_ordering()
    {
        var fromLatLng = GeoPoint.FromLatLng(38.7223, -9.1393);
        var fromGeoJson = GeoPoint.FromGeoJson(-9.1393, 38.7223);
        var fromArray = GeoPoint.FromGeoJson(new[] { -9.1393, 38.7223 });

        Assert.Equal(38.7223, fromLatLng.Latitude);
        Assert.Equal(-9.1393, fromLatLng.Longitude);
        Assert.Equal(fromLatLng, fromGeoJson);
        Assert.Equal(fromLatLng, fromArray);
    }

    [Theory]
    [InlineData(new double[] { 1.0 })]
    [InlineData(new double[] { 1.0, 2.0, 3.0 })]
    public void FromGeoJson_rejects_wrong_length(double[] coordinates)
    {
        Assert.Throws<ArgumentException>(() => GeoPoint.FromGeoJson(coordinates));
    }

    [Theory]
    [InlineData(91.0, 0.0)]
    [InlineData(-91.0, 0.0)]
    [InlineData(0.0, 181.0)]
    [InlineData(0.0, -181.0)]
    public void FromLatLng_rejects_out_of_range(double latitude, double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GeoPoint.FromLatLng(latitude, longitude));
    }

    [Fact]
    public void DistanceKm_lisbon_to_porto_is_about_274()
    {
        var lisbon = GeoPoint.FromLatLng(38.7223, -9.1393);
        var porto = GeoPoint.FromLatLng(41.1579, -8.6291);

        Assert.Equal(274, lisbon.DistanceKm(porto), 5.0);
    }
}
