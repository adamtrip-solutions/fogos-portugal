using System.Text.Json;
using Fogos.Domain.Incidents;
using MongoDB.Driver;

namespace Fogos.Integration.Tests;

[Collection("fogos")]
public sealed class ReadApiTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Incidents_and_incident_return_expected_shape_with_status_color()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("F1", statusCode: IncidentStatusCatalog.EmCurso));

        var byQuery = await fixture.GraphQLAsync("{ incidents(first:5){ nodes { id kind status { code label color } } pageInfo { hasNextPage } } }");
        var node = byQuery.RootElement.GetProperty("data").GetProperty("incidents").GetProperty("nodes")[0];
        Assert.Equal("F1", node.GetProperty("id").GetString());
        Assert.Equal("FIRE", node.GetProperty("kind").GetString());
        Assert.Equal(IncidentStatusCatalog.EmCurso, node.GetProperty("status").GetProperty("code").GetInt32());
        Assert.Equal("B81E1F", node.GetProperty("status").GetProperty("color").GetString());

        var single = await fixture.GraphQLAsync(
            "query($id:ID!){ incident(id:$id){ id concelho } }", new { id = "F1" });
        Assert.Equal("F1", single.RootElement.GetProperty("data").GetProperty("incident").GetProperty("id").GetString());
    }

    [SkippableFact]
    public async Task Incidents_totalCount_covers_all_pages_regardless_of_cursor()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertManyAsync(Enumerable.Range(1, 7).Select(i =>
            SeedData.Incident($"TC{i}", IncidentKind.Fire)));
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("TC_FMA", IncidentKind.Fma));

        // Default filter is fire-only: totalCount is 7 even though the page holds 3.
        var page1 = await fixture.GraphQLAsync(
            "{ incidents(first:3){ totalCount nodes { id } pageInfo { hasNextPage endCursor } } }");
        var conn = page1.RootElement.GetProperty("data").GetProperty("incidents");
        Assert.Equal(7, conn.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, conn.GetProperty("nodes").GetArrayLength());
        Assert.True(conn.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());

        // totalCount ignores the after-cursor: page 2 reports the same total.
        var cursor = conn.GetProperty("pageInfo").GetProperty("endCursor").GetString();
        var page2 = await fixture.GraphQLAsync(
            "query($after:String){ incidents(first:3, after:$after){ totalCount nodes { id } } }",
            new { after = cursor });
        Assert.Equal(7, page2.RootElement.GetProperty("data").GetProperty("incidents").GetProperty("totalCount").GetInt32());

        // With all=true the FMA joins the count.
        var all = await fixture.GraphQLAsync("{ incidents(filter:{ all: true }, first:3){ totalCount } }");
        Assert.Equal(8, all.RootElement.GetProperty("data").GetProperty("incidents").GetProperty("totalCount").GetInt32());
    }

    [SkippableFact]
    public async Task ActiveIncidents_defaults_to_fire_only()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertManyAsync(
        [
            SeedData.Incident("FIRE1", IncidentKind.Fire, active: true),
            SeedData.Incident("URBAN1", IncidentKind.UrbanFire, active: true),
            SeedData.Incident("FIRE_INACTIVE", IncidentKind.Fire, active: false),
        ]);

        var doc = await fixture.GraphQLAsync("{ activeIncidents { id kind } }");
        var nodes = doc.RootElement.GetProperty("data").GetProperty("activeIncidents").EnumerateArray().ToList();

        Assert.Single(nodes);
        Assert.Equal("FIRE1", nodes[0].GetProperty("id").GetString());
        Assert.All(nodes, n => Assert.Equal("FIRE", n.GetProperty("kind").GetString()));
    }

    [SkippableFact]
    public async Task IncidentFilter_day_concelho_and_all_are_honoured()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);

        var june15 = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(1));
        var june14 = new DateTimeOffset(2026, 6, 14, 12, 0, 0, TimeSpan.FromHours(1));
        await ctx.Incidents.InsertManyAsync(
        [
            SeedData.Incident("D15", occurredAt: june15, concelho: "Porto"),
            SeedData.Incident("D14", occurredAt: june14, concelho: "Lisboa"),
            SeedData.Incident("URB", IncidentKind.UrbanFire, occurredAt: june15, concelho: "Porto"),
        ]);

        // day filter → only the June 15 fires (fire-only default excludes URB).
        var day = await Ids(await fixture.GraphQLAsync(
            "{ incidents(filter:{ day:\"2026-06-15\" }){ nodes { id } } }"));
        Assert.Equal(new[] { "D15" }, day.Order().ToArray());

        // concelho filter.
        var porto = await Ids(await fixture.GraphQLAsync(
            "{ incidents(filter:{ concelho:\"Porto\" }){ nodes { id } } }"));
        Assert.Equal(new[] { "D15" }, porto.Order().ToArray());

        // all:true removes the fire-only restriction.
        var all = await Ids(await fixture.GraphQLAsync(
            "{ incidents(filter:{ all:true }){ nodes { id } } }"));
        Assert.Equal(new[] { "D14", "D15", "URB" }, all.Order().ToArray());

        // default (no all) is fire-only.
        var fireOnly = await Ids(await fixture.GraphQLAsync(
            "{ incidents(filter:{ }){ nodes { id } } }"));
        Assert.Equal(new[] { "D14", "D15" }, fireOnly.Order().ToArray());
    }

    [SkippableFact]
    public async Task IncidentFilter_statusCodes_district_and_combined_are_honoured()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);

        var june15 = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(1));
        var june14 = new DateTimeOffset(2026, 6, 14, 12, 0, 0, TimeSpan.FromHours(1));
        await ctx.Incidents.InsertManyAsync(
        [
            SeedData.Incident("A", occurredAt: june15, district: "Viseu", statusCode: IncidentStatusCatalog.EmCurso),
            SeedData.Incident("B", occurredAt: june15, district: "Viseu", statusCode: IncidentStatusCatalog.ChegadaAoTeatroDeOperacoes),
            SeedData.Incident("C", occurredAt: june14, district: "Porto", statusCode: IncidentStatusCatalog.Conclusao),
            SeedData.Incident("D", occurredAt: june15, district: "Porto", statusCode: IncidentStatusCatalog.EmCurso),
        ]);

        // statusCodes → only the matching statuses (5, 6); status 8 excluded.
        var byStatus = await Ids(await fixture.GraphQLAsync(
            "{ incidents(filter:{ statusCodes:[5,6] }){ nodes { id } } }"));
        Assert.Equal(new[] { "A", "B", "D" }, byStatus.Order().ToArray());

        // Null statusCodes (omitted) is unconstrained.
        var nullStatus = await Ids(await fixture.GraphQLAsync(
            "{ incidents(filter:{ }){ nodes { id } } }"));
        Assert.Equal(new[] { "A", "B", "C", "D" }, nullStatus.Order().ToArray());

        // Explicit empty statusCodes list is also unconstrained (not "match nothing").
        var emptyStatus = await Ids(await fixture.GraphQLAsync(
            "{ incidents(filter:{ statusCodes:[] }){ nodes { id } } }"));
        Assert.Equal(new[] { "A", "B", "C", "D" }, emptyStatus.Order().ToArray());

        // district → exact match; other districts excluded.
        var byDistrict = await Ids(await fixture.GraphQLAsync(
            "{ incidents(filter:{ district:\"Viseu\" }){ nodes { id } } }"));
        Assert.Equal(new[] { "A", "B" }, byDistrict.Order().ToArray());

        // Combined statusCodes + after + district AND-composes: only A satisfies all three.
        var combined = await Ids(await fixture.GraphQLAsync(
            "{ incidents(filter:{ statusCodes:[5], after:\"2026-06-15\", district:\"Viseu\" }){ nodes { id } } }"));
        Assert.Equal(new[] { "A" }, combined.Order().ToArray());
    }

    [SkippableFact]
    public async Task Ten_incidents_with_photos_and_weather_resolve_without_error()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);

        await ctx.WeatherStations.InsertOneAsync(SeedData.Station(1));
        await ctx.WeatherHourly.InsertOneAsync(SeedData.Observation(1));

        var incidents = Enumerable.Range(0, 10)
            .Select(i => SeedData.Incident($"B{i}", nearestStation: 1))
            .ToList();
        await ctx.Incidents.InsertManyAsync(incidents);
        await ctx.IncidentPhotos.InsertManyAsync(
        [
            SeedData.Photo("B0"),
            SeedData.Photo("B0"),
            SeedData.Photo("B1"),
        ]);

        var doc = await fixture.GraphQLAsync(
            "{ incidents(first:10){ nodes { id photos { id publicUrl } weather { stationName distanceKm } } } }");

        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        var nodes = doc.RootElement.GetProperty("data").GetProperty("incidents").GetProperty("nodes");
        Assert.Equal(10, nodes.GetArrayLength());

        var b0 = nodes.EnumerateArray().First(n => n.GetProperty("id").GetString() == "B0");
        Assert.Equal(2, b0.GetProperty("photos").GetArrayLength());
        Assert.StartsWith("https://cdn.example.test/", b0.GetProperty("photos")[0].GetProperty("publicUrl").GetString());
        Assert.Equal("Lisboa (Geofísico)", b0.GetProperty("weather").GetProperty("stationName").GetString());
    }

    private static Task<List<string>> Ids(JsonDocument doc)
    {
        var ids = doc.RootElement.GetProperty("data").GetProperty("incidents").GetProperty("nodes")
            .EnumerateArray().Select(n => n.GetProperty("id").GetString()!).ToList();
        return Task.FromResult(ids);
    }
}
