using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;

namespace Fogos.Domain.Tests;

public class IgnitionClusteringTests
{
    private static IgnitionClustering.Point P(string id, double lat, double lng) =>
        new(id, GeoPoint.FromLatLng(lat, lng));

    [Fact]
    public void Nearby_points_form_one_group_and_a_far_point_stays_separate()
    {
        var points = new[]
        {
            P("A", 40.00, -8.00),
            P("B", 40.02, -8.00), // ~2.2 km from A
            P("C", 40.00, -8.02), // ~1.7 km from A
            P("FAR", 41.50, -8.00), // ~167 km away
        };

        var groups = IgnitionClustering.Group(points, linkKm: 10);

        var big = Assert.Single(groups, g => g.Count == 3);
        Assert.Equal(["A", "B", "C"], big.Select(p => p.IncidentId).OrderBy(x => x));
        Assert.Single(groups, g => g.Count == 1 && g[0].IncidentId == "FAR");
    }

    [Fact]
    public void Single_linkage_chains_points_that_are_not_pairwise_close()
    {
        // A—B ≈ 8.3 km, B—C ≈ 8.3 km, but A—C ≈ 16.7 km (> 10). Single-linkage still joins all three.
        var points = new[]
        {
            P("A", 40.00, -8.00),
            P("B", 40.075, -8.00),
            P("C", 40.15, -8.00),
        };

        var groups = IgnitionClustering.Group(points, linkKm: 10);

        var one = Assert.Single(groups);
        Assert.Equal(3, one.Count);
    }

    [Fact]
    public void Centroid_is_the_arithmetic_mean()
    {
        var centroid = IgnitionClustering.Centroid(
        [
            P("A", 40.0, -8.0),
            P("B", 42.0, -8.0),
            P("C", 41.0, -7.0),
        ]);

        Assert.Equal(41.0, centroid.Latitude, 6);
        Assert.Equal(-7.6666667, centroid.Longitude, 5);
    }

    [Fact]
    public void Empty_input_yields_no_groups()
    {
        Assert.Empty(IgnitionClustering.Group([], linkKm: 10));
    }
}
