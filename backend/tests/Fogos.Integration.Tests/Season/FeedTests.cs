using System.Net;
using System.Xml.Linq;
using Fogos.Domain.Geo;
using Fogos.Domain.Warnings;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Season;

/// <summary>
/// RSS / GeoRSS feeds: well-formed XML, a georss point per located fire, correct XML escaping of a
/// location containing an ampersand, and the 60 s cache header.
/// </summary>
[Collection("fogos")]
public sealed class FeedTests(ContainerFixture fixture)
{
    private static readonly XNamespace GeoRss = "http://www.georss.org/georss";

    [SkippableFact]
    public async Task Incidents_rss_is_well_formed_with_georss_and_escapes_ampersands()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);

        var fire = SeedData.Incident("RSS1", active: true, concelho: "São João & Maria",
            coordinates: GeoPoint.FromLatLng(40.1, -8.2));
        await ctx.Incidents.InsertOneAsync(fire);

        var client = fixture.Factory.CreateClient();
        var response = await client.GetAsync("/v3/feeds/incidents.rss");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/rss+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("&amp;", text);                 // raw XML escapes the ampersand
        Assert.DoesNotContain("& Maria", text);         // never a bare, unescaped ampersand

        var doc = XDocument.Parse(text);                // throws if not well-formed
        Assert.Equal("rss", doc.Root!.Name.LocalName);
        Assert.NotNull(doc.Descendants(GeoRss + "point").FirstOrDefault());

        var title = doc.Descendants("item").First().Element("title")!.Value;
        Assert.Contains("São João & Maria", title);     // parsed value carries the real ampersand
    }

    [SkippableFact]
    public async Task Warnings_rss_is_well_formed()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Warnings.DeleteManyAsync(FilterDefinition<Warning>.Empty);

        await ctx.Warnings.InsertOneAsync(new Warning
        {
            Kind = WarningKind.Manual,
            Message = "Cuidado com o vento & o fumo",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var client = fixture.Factory.CreateClient();
        var response = await client.GetAsync("/v3/feeds/warnings.rss");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/rss+xml", response.Content.Headers.ContentType?.MediaType);

        var text = await response.Content.ReadAsStringAsync();
        var doc = XDocument.Parse(text);
        Assert.Equal("rss", doc.Root!.Name.LocalName);
        var title = doc.Descendants("item").Single().Element("title")!.Value;
        Assert.Equal("Cuidado com o vento & o fumo", title);
    }
}
