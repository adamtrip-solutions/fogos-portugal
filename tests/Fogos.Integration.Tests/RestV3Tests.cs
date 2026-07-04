using System.Net;
using System.Xml.Linq;
using Fogos.Domain.Geo;

namespace Fogos.Integration.Tests;

[Collection("fogos")]
public sealed class RestV3Tests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Active_geojson_csv_kml_return_correct_content_types()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("R1", active: true));
        var client = fixture.Factory.CreateClient();

        // GeoJSON
        var geo = await client.GetAsync("/v3/incidents/active.geojson");
        Assert.Equal(HttpStatusCode.OK, geo.StatusCode);
        Assert.Equal("application/geo+json", geo.Content.Headers.ContentType?.MediaType);
        Assert.Contains("FeatureCollection", await geo.Content.ReadAsStringAsync());
        Assert.Equal("public, max-age=15, s-maxage=30, stale-while-revalidate=30", geo.Headers.CacheControl?.ToString());

        // CSV — UTF-8 BOM + ';' delimiter
        var csv = await client.GetAsync("/v3/incidents/active.csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "CSV must start with a UTF-8 BOM");
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains(";", text);
        Assert.Contains("R1", text);

        // KML — valid XML
        var kml = await client.GetAsync("/v3/incidents/active.kml");
        Assert.Equal(HttpStatusCode.OK, kml.StatusCode);
        Assert.Equal("application/vnd.google-earth.kml+xml", kml.Content.Headers.ContentType?.MediaType);
        var kmlText = await kml.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(kmlText); // throws if not well-formed XML
        Assert.Equal("kml", doc.Root!.Name.LocalName);
        // The declared encoding must match the UTF-8 bytes we serve (was wrongly "utf-16").
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", kmlText);
        Assert.DoesNotContain("utf-16", kmlText, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task KmlFirms_404_without_hotspots_and_200_with_polygon()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        var client = fixture.Factory.CreateClient();

        // No hotspots → only the incident point → hull < 3 → 404.
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("FN", coordinates: GeoPoint.FromLatLng(40.0, -8.0)));
        var missing = await client.GetAsync("/v3/incidents/FN/kml-firms");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // Incident + 3 spread hotspots → hull ≥ 3 → 200 with a polygon.
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("FK", coordinates: GeoPoint.FromLatLng(40.0, -8.0)));
        await ctx.Hotspots.InsertOneAsync(SeedData.HotspotsDoc("FK",
        [
            GeoPoint.FromLatLng(40.05, -8.05),
            GeoPoint.FromLatLng(40.05, -7.95),
            GeoPoint.FromLatLng(39.95, -8.00),
        ]));

        var firms = await client.GetAsync("/v3/incidents/FK/kml-firms");
        Assert.Equal(HttpStatusCode.OK, firms.StatusCode);
        Assert.Equal("application/vnd.google-earth.kml+xml", firms.Content.Headers.ContentType?.MediaType);
        var kmlText = await firms.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(kmlText);
        XNamespace ns = "http://www.opengis.net/kml/2.2";
        Assert.NotNull(doc.Descendants(ns + "Polygon").FirstOrDefault());
    }

    [SkippableFact]
    public async Task Stored_kml_is_404_when_absent()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("NK"));
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/v3/incidents/NK/kml");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
