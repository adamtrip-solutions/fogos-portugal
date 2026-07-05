using System.Text.Json;
using Fogos.Domain.Risk;
using Fogos.Infrastructure.Mongo;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Risk;

[Collection("fogos")]
public sealed class RcmProcessorTests(ContainerFixture fixture)
{
    private static readonly DateOnly ForecastDate = new(2026, 7, 4);

    private const string Page = """
        <html><body><script>
        rcmF[0] = {"dataPrev":"2026-07-04","dataRun":"2026-07-04 09:00","fileDate":"20260704","local":{"0101":{"data":{"dico":"0101","rcm":3}},"1106":{"data":{"dico":"1106","rcm":5}},"1312":{"data":{"dico":"1312","rcm":2}}}};
        rcmF[1] = {"dataPrev":"2026-07-04","local":{"0101":{"data":{"rcm":4}},"1106":{"data":{"rcm":2}},"1312":{"data":{"rcm":1}}}};
        rcmF[2] = {"dataPrev":"2026-07-04","local":{"0101":{"data":{"rcm":2}},"1106":{"data":{"rcm":3}},"1312":{"data":{"rcm":1}}}};
        rcmF[3] = {"dataPrev":"2026-07-04","local":{"0101":{"data":{"rcm":1}},"1106":{"data":{"rcm":1}},"1312":{"data":{"rcm":1}}}};
        rcmF[4] = {"dataPrev":"2026-07-04","local":{"0101":{"data":{"rcm":1}},"1106":{"data":{"rcm":1}},"1312":{"data":{"rcm":1}}}};
        </script></body></html>
        """;

    private async Task<MongoContext> ResetAsync()
    {
        var ctx = fixture.Factory.Services.GetRequiredService<MongoContext>();
        await ctx.RcmDaily.DeleteManyAsync(FilterDefinition<ConcelhoRisk>.Empty);
        await ctx.RcmGeoJson.DeleteManyAsync(FilterDefinition<RiskGeoJson>.Empty);
        return ctx;
    }

    [SkippableFact]
    public async Task Process_upserts_daily_rows_with_all_horizon_levels()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        var ctx = await ResetAsync();
        var processor = RiskTestHost.BuildProcessor(ctx);

        await processor.ProcessAsync(Page);

        var lisboa = await ctx.RcmDaily
            .Find(Builders<ConcelhoRisk>.Filter.Eq(x => x.Dico, "1106") & Builders<ConcelhoRisk>.Filter.Eq(x => x.Date, ForecastDate))
            .FirstOrDefaultAsync();

        Assert.NotNull(lisboa);
        Assert.Equal("Lisboa", lisboa!.Concelho);
        Assert.Equal(5, lisboa.Today);
        Assert.Equal(2, lisboa.Tomorrow);
        Assert.Equal(3, lisboa.After);
        Assert.Equal(1, lisboa.After2);
        Assert.Equal(1, lisboa.After3);

        // Every concelho in the polygon set got a row.
        var count = await ctx.RcmDaily.CountDocumentsAsync(Builders<ConcelhoRisk>.Filter.Eq(x => x.Date, ForecastDate));
        Assert.Equal(278, count);
    }

    [SkippableFact]
    public async Task Process_builds_geojson_horizons_carrying_risk_properties()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        var ctx = await ResetAsync();
        var processor = RiskTestHost.BuildProcessor(ctx);

        await processor.ProcessAsync(Page);

        var today = await ctx.RcmGeoJson
            .Find(Builders<RiskGeoJson>.Filter.Eq(x => x.When, RiskDay.Today))
            .FirstOrDefaultAsync();
        Assert.NotNull(today);

        using var doc = JsonDocument.Parse(today!.GeoJson);
        var lisboaFeature = doc.RootElement.GetProperty("features").EnumerateArray()
            .First(f => f.GetProperty("properties").GetProperty("DICO").GetString() == "1106");
        Assert.Equal(5, lisboaFeature.GetProperty("properties").GetProperty("data").GetProperty("rcm").GetInt32());

        // Three served horizons, one doc each.
        Assert.Equal(3, await ctx.RcmGeoJson.CountDocumentsAsync(FilterDefinition<RiskGeoJson>.Empty));
    }

    [SkippableFact]
    public async Task Process_is_idempotent_on_rerun()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        var ctx = await ResetAsync();
        var processor = RiskTestHost.BuildProcessor(ctx);

        await processor.ProcessAsync(Page);
        await processor.ProcessAsync(Page);

        Assert.Equal(278, await ctx.RcmDaily.CountDocumentsAsync(Builders<ConcelhoRisk>.Filter.Eq(x => x.Date, ForecastDate)));
        Assert.Equal(3, await ctx.RcmGeoJson.CountDocumentsAsync(FilterDefinition<RiskGeoJson>.Empty));
    }
}
