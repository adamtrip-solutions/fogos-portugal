using Fogos.Domain.Geo;

namespace Fogos.Domain.Tests;

public class ConvexHullTests
{
    private static readonly GeoPoint[] SquareWithInterior =
    [
        GeoPoint.FromLatLng(0, 0),
        GeoPoint.FromLatLng(0, 1),
        GeoPoint.FromLatLng(1, 0),
        GeoPoint.FromLatLng(1, 1),
        GeoPoint.FromLatLng(0.5, 0.5),
    ];

    [Fact]
    public void Fewer_than_three_points_yields_empty()
    {
        Assert.Empty(ConvexHull.Compute([GeoPoint.FromLatLng(0, 0), GeoPoint.FromLatLng(1, 1)]));
    }

    [Fact]
    public void Square_with_interior_point_yields_four_hull_vertices()
    {
        var hull = ConvexHull.Compute(SquareWithInterior, bufferDegrees: 0);

        Assert.Equal(4, hull.Count);
        Assert.DoesNotContain(GeoPoint.FromLatLng(0.5, 0.5), hull);
    }

    [Fact]
    public void Buffered_hull_is_strictly_larger()
    {
        var tight = ConvexHull.Compute(SquareWithInterior, bufferDegrees: 0);
        var buffered = ConvexHull.Compute(SquareWithInterior);

        Assert.True(buffered.Max(p => p.Latitude) > tight.Max(p => p.Latitude));
        Assert.True(buffered.Min(p => p.Latitude) < tight.Min(p => p.Latitude));
        Assert.True(buffered.Max(p => p.Longitude) > tight.Max(p => p.Longitude));
        Assert.True(buffered.Min(p => p.Longitude) < tight.Min(p => p.Longitude));
    }
}
