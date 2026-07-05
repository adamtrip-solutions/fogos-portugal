using System.Net;
using Fogos.Domain.Auth;
using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Rendering;
using Fogos.Worker.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Writes;

/// <summary>
/// <c>attachKml</c> operator mutation: stores the perimeter in the ANEPC slot (or the VOST slot when
/// <c>vost:true</c>), rejects non-KML payloads, and flips the <c>hasKml</c>/<c>hasKmlVost</c> read flags.
/// The perimeter announcement (renderer + tweet) is exercised through the worker handler, dry-run.
/// </summary>
[Collection("fogos")]
public sealed class AttachKmlTests(ContainerFixture fixture)
{
    private const string OperatorKey = "fgs_live_operator_incidents_kml";
    private const string SampleKml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document><name>Perímetro</name></Document></kml>";

    private async Task SeedOperatorAsync() =>
        await SeedData.InsertApiKeyAsync(fixture, OperatorKey, ApiTier.Operator,
            name: "kml operator", scopes: [ApiScopes.WriteIncidents]);

    [SkippableFact]
    public async Task Attach_stores_kml_and_flips_hasKml()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("KML1"));
        await SeedOperatorAsync();

        var doc = await fixture.GraphQLAsync(OperatorKey,
            "mutation($id:ID!,$kml:String!){ attachKml(incidentId:$id, kml:$kml){ id hasKml hasKmlVost } }",
            new { id = "KML1", kml = SampleKml });

        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        var result = doc.RootElement.GetProperty("data").GetProperty("attachKml");
        Assert.True(result.GetProperty("hasKml").GetBoolean());
        Assert.False(result.GetProperty("hasKmlVost").GetBoolean());

        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "KML1")).SingleAsync();
        Assert.Equal(SampleKml, stored.Kml);
        Assert.Null(stored.KmlVost);
    }

    [SkippableFact]
    public async Task Attach_vost_stores_into_kmlVost()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("KML2"));
        await SeedOperatorAsync();

        var doc = await fixture.GraphQLAsync(OperatorKey,
            "mutation($id:ID!,$kml:String!){ attachKml(incidentId:$id, kml:$kml, vost:true){ hasKml hasKmlVost } }",
            new { id = "KML2", kml = SampleKml });

        var result = doc.RootElement.GetProperty("data").GetProperty("attachKml");
        Assert.False(result.GetProperty("hasKml").GetBoolean());
        Assert.True(result.GetProperty("hasKmlVost").GetBoolean());

        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "KML2")).SingleAsync();
        Assert.Equal(SampleKml, stored.KmlVost);
        Assert.Null(stored.Kml);
    }

    [SkippableFact]
    public async Task Invalid_xml_is_rejected()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("KML3"));
        await SeedOperatorAsync();

        var doc = await fixture.GraphQLAsync(OperatorKey,
            "mutation($id:ID!,$kml:String!){ attachKml(incidentId:$id, kml:$kml){ id } }",
            new { id = "KML3", kml = "this is not <kml" });

        Assert.True(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        Assert.Contains("KML_INVALID", doc.RootElement.GetProperty("errors").ToString());

        // Non-kml-rooted XML is also rejected.
        var wrongRoot = await fixture.GraphQLAsync(OperatorKey,
            "mutation($id:ID!,$kml:String!){ attachKml(incidentId:$id, kml:$kml){ id } }",
            new { id = "KML3", kml = "<gpx><trk/></gpx>" });
        Assert.Contains("KML_INVALID", wrongRoot.RootElement.GetProperty("errors").ToString());

        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "KML3")).SingleAsync();
        Assert.Null(stored.Kml);
    }

    [SkippableFact]
    public async Task Perimeter_post_is_captured_and_survives_renderer_failure()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("KML4"));

        var ops = new RecordingOps();
        var handler = BuildHandler(ops);
        await handler.HandleAsync(new KmlAttached("KML4", Vost: false), CancellationToken.None);

        var tweet = ops.Captures.Single(c => c.Channel == "twitter").Payload;
        Assert.Contains("Nova área de interesse por @VostPT", tweet);
        Assert.Contains("https://fogos.pt/fogo/KML4/detalhe", tweet);
        Assert.Contains("image=no", tweet); // renderer failed → text-only, post still went out
    }

    private KmlAttachedSocialHandler BuildHandler(RecordingOps ops)
    {
        var services = fixture.Factory.Services;
        var mongo = services.GetRequiredService<MongoContext>();
        var clock = services.GetRequiredService<IClock>();
        var threads = new SocialThreadStore(mongo, clock);

        var publishing = Options.Create(new PublishingOptions()); // DryRun defaults
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(_ => new HttpResponseMessage()));
        var twitter = new TwitterPublisher(factory, publishing, Options.Create(new TwitterOptions()), ops, NullLogger<TwitterPublisher>.Instance);

        // Renderer that always fails → the post degrades to text-only.
        var failingRenderer = new RendererClient(
            new StubHttpClientFactory(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))),
            Options.Create(new RendererOptions { Url = "http://renderer.invalid", ScreenshotDomain = "fogos.pt", Retries = 1, RetryBaseDelay = TimeSpan.Zero, MinBytes = 1 }),
            ops, NullLogger<RendererClient>.Instance);

        return new KmlAttachedSocialHandler(mongo, threads, twitter, failingRenderer,
            Options.Create(new IncidentPipelineOptions { SocialLinkDomain = "fogos.pt" }));
    }
}
