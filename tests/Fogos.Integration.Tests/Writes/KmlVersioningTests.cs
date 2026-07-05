using System.Net;
using Fogos.Domain.Auth;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Incidents;
using Fogos.Infrastructure.Mongo;
using Fogos.Integration.Tests.Incidents;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Writes;

/// <summary>
/// KML perimeter versioning: <see cref="KmlVersionStore"/> dedups identical KML per (incident, slot)
/// and appends a new version when the content changes; the attachKml mutation feeds through it; and the
/// REST endpoints expose the metadata list and the immutable per-version KML.
/// </summary>
[Collection("fogos")]
public sealed class KmlVersioningTests(ContainerFixture fixture)
{
    private const string OperatorKey = "fgs_live_operator_kmlver";
    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private static string Kml(string name) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document><name>{name}</name></Document></kml>";

    private async Task ResetAsync()
    {
        await SeedData.ResetAsync(fixture);
        await SeedData.Context(fixture).IncidentKmlVersions.DeleteManyAsync(FilterDefinition<IncidentKmlVersion>.Empty);
    }

    [SkippableFact]
    public async Task Store_dedups_identical_kml_and_versions_changed_kml()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);
        var store = new KmlVersionStore(ctx, new TestClock { UtcNow = T0 });

        // First write → v1.
        Assert.NotNull(await store.AppendIfChangedAsync("KV1", vost: false, Kml("A"), CancellationToken.None));
        // Identical → deduped (null).
        Assert.Null(await store.AppendIfChangedAsync("KV1", vost: false, Kml("A"), CancellationToken.None));
        // Changed → v2.
        Assert.NotNull(await store.AppendIfChangedAsync("KV1", vost: false, Kml("B"), CancellationToken.None));
        // Same content but different slot → distinct version.
        Assert.NotNull(await store.AppendIfChangedAsync("KV1", vost: true, Kml("B"), CancellationToken.None));
        // Empty payload → no version.
        Assert.Null(await store.AppendIfChangedAsync("KV1", vost: false, "", CancellationToken.None));

        var all = await ctx.IncidentKmlVersions
            .Find(Builders<IncidentKmlVersion>.Filter.Eq(x => x.IncidentId, "KV1")).ToListAsync();
        Assert.Equal(3, all.Count);
        Assert.Equal(2, all.Count(v => !v.Vost));
        Assert.Single(all, v => v.Vost);
        Assert.All(all, v => Assert.True(v.SizeBytes > 0));
    }

    [SkippableFact]
    public async Task AttachKml_mutation_appends_a_version()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("KV2"));
        await SeedData.InsertApiKeyAsync(fixture, OperatorKey, ApiTier.Operator, scopes: [ApiScopes.WriteIncidents]);

        await fixture.GraphQLAsync(OperatorKey,
            "mutation($id:ID!,$kml:String!){ attachKml(incidentId:$id, kml:$kml){ id } }",
            new { id = "KV2", kml = Kml("Perimeter") });

        var versions = await ctx.IncidentKmlVersions
            .Find(Builders<IncidentKmlVersion>.Filter.Eq(x => x.IncidentId, "KV2")).ToListAsync();
        var v = Assert.Single(versions);
        Assert.False(v.Vost);
    }

    [SkippableFact]
    public async Task Rest_lists_versions_and_serves_immutable_kml()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);
        var store = new KmlVersionStore(ctx, new TestClock { UtcNow = T0 });
        var v1 = await store.AppendIfChangedAsync("KV3", vost: false, Kml("A"), CancellationToken.None);
        var v2 = await store.AppendIfChangedAsync("KV3", vost: false, Kml("B"), CancellationToken.None);
        var client = fixture.Factory.CreateClient();

        // List (JSON meta, no raw KML).
        var list = await client.GetAsync("/v3/incidents/KV3/kml-versions");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await list.Content.ReadAsStringAsync();
        Assert.Contains(v1!.Id, body);
        Assert.Contains(v2!.Id, body);
        Assert.Contains("sizeBytes", body);
        Assert.DoesNotContain("<kml", body);
        Assert.Equal("public, max-age=60", list.Headers.CacheControl?.ToString());

        // Single version content (KML, immutable).
        var one = await client.GetAsync($"/v3/incidents/KV3/kml-versions/{v2.Id}");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal("application/vnd.google-earth.kml+xml", one.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", one.Headers.CacheControl?.ToString() ?? "");
        Assert.Contains("<name>B</name>", await one.Content.ReadAsStringAsync());

        // Unknown version → 404.
        var missing = await client.GetAsync("/v3/incidents/KV3/kml-versions/deadbeefdeadbeefdeadbeef");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // Malformed (non-ObjectId) version → 404, not a 500 from a serialization throw.
        var malformed = await client.GetAsync("/v3/incidents/KV3/kml-versions/notanid");
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
    }

    [SkippableFact]
    public async Task GraphQL_exposes_kml_history_metadata()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("KV4"));
        var store = new KmlVersionStore(ctx, new TestClock { UtcNow = T0 });
        await store.AppendIfChangedAsync("KV4", vost: false, Kml("A"), CancellationToken.None);
        await store.AppendIfChangedAsync("KV4", vost: true, Kml("B"), CancellationToken.None);

        using var doc = await fixture.GraphQLAsync(
            "query($id: ID!){ incident(id:$id){ kmlHistory { id vost capturedAt sizeBytes } } }",
            new { id = "KV4" });
        var history = doc.RootElement.GetProperty("data").GetProperty("incident").GetProperty("kmlHistory");
        Assert.Equal(2, history.GetArrayLength());
        Assert.All(history.EnumerateArray(), e => Assert.True(e.GetProperty("sizeBytes").GetInt32() > 0));
    }
}
