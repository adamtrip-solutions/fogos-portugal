using Fogos.Infrastructure.Geo;

namespace Fogos.Integration.Tests.Geo;

/// <summary>
/// Pure (container-free) tests for the coordinate → concelho point-in-polygon lookup that backs the ingest
/// location fallback. Coordinates were cross-checked against the embedded polygon properties, not assumed.
/// </summary>
public sealed class ConcelhoLocatorTests
{
    private static readonly ConcelhoLocator Locator = new();

    [Fact]
    public void Point_inside_a_concelho_returns_its_dico_name_and_district()
    {
        // The Vouzela fire coordinates fall inside the VOUZELA feature (DICO 1824, Viseu) — verified against
        // the geojson properties.
        var match = Locator.Locate(40.680513, -8.15205);

        Assert.NotNull(match);
        Assert.Equal("1824", match!.Dico);
        Assert.Equal("VOUZELA", match.Concelho);   // raw (uppercase) polygon name; the resolver title-cases it
        Assert.Equal("VISEU", match.Distrito);
    }

    [Fact]
    public void Point_inside_a_multipolygon_concelho_resolves()
    {
        // MONTIJO (DICO 1507, Setúbal) is a MultiPolygon (Tagus estuary split); this interior point was
        // verified to fall inside one of its polygons.
        var match = Locator.Locate(38.6878034, -9.04704565);

        Assert.NotNull(match);
        Assert.Equal("1507", match!.Dico);
        Assert.Equal("MONTIJO", match.Concelho);
    }

    [Fact]
    public void Point_in_the_atlantic_returns_null()
    {
        Assert.Null(Locator.Locate(39.0, -20.0));
    }

    [Fact]
    public void Point_in_spain_returns_null()
    {
        // Madrid — well outside every mainland Portuguese concelho.
        Assert.Null(Locator.Locate(40.4168, -3.7038));
    }
}
