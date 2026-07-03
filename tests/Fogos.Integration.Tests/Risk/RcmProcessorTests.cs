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
        var (processor, _) = RiskTestHost.BuildProcessor(ctx, rendererSucceeds: true);

        await processor.ProcessAsync(Page, publishSocial: false, tomorrow: false);

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
        var (processor, _) = RiskTestHost.BuildProcessor(ctx, rendererSucceeds: true);

        await processor.ProcessAsync(Page, publishSocial: false, tomorrow: false);

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
        var (processor, _) = RiskTestHost.BuildProcessor(ctx, rendererSucceeds: true);

        await processor.ProcessAsync(Page, publishSocial: false, tomorrow: false);
        await processor.ProcessAsync(Page, publishSocial: false, tomorrow: false);

        Assert.Equal(278, await ctx.RcmDaily.CountDocumentsAsync(Builders<ConcelhoRisk>.Filter.Eq(x => x.Date, ForecastDate)));
        Assert.Equal(3, await ctx.RcmGeoJson.CountDocumentsAsync(FilterDefinition<RiskGeoJson>.Empty));
    }

    [SkippableFact]
    public async Task Social_run_captures_dry_run_posts_on_all_channels()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        var ctx = await ResetAsync();
        var (processor, ops) = RiskTestHost.BuildProcessor(ctx, rendererSucceeds: true);

        await processor.ProcessAsync(Page, publishSocial: true, tomorrow: false);

        var channels = ops.Captures.Select(c => c.Channel).ToHashSet();
        Assert.Contains("twitter", channels);
        Assert.Contains("telegram", channels);
        Assert.Contains("facebook", channels);
        // Lisboa (1106) is at Máximo today → it must appear in the captured post text.
        Assert.Contains(ops.Captures, c => c.Payload.Contains("Lisboa") && c.Payload.Contains("Máximo"));
    }

    [SkippableFact]
    public async Task No_social_run_captures_nothing()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        var ctx = await ResetAsync();
        var (processor, ops) = RiskTestHost.BuildProcessor(ctx, rendererSucceeds: true);

        await processor.ProcessAsync(Page, publishSocial: false, tomorrow: false);

        Assert.Empty(ops.Captures);
    }

    [SkippableFact]
    public async Task Social_run_degrades_to_text_only_when_renderer_fails()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        var ctx = await ResetAsync();
        var (processor, ops) = RiskTestHost.BuildProcessor(ctx, rendererSucceeds: false);

        // Must not throw even though the renderer 500s.
        await processor.ProcessAsync(Page, publishSocial: true, tomorrow: false);

        // Posts still went out (captured), and none is flagged as carrying an image.
        Assert.Contains(ops.Captures, c => c.Channel == "twitter");
        Assert.DoesNotContain(ops.Captures, c => c.Payload.StartsWith("[img]"));
        // The renderer failure was escalated to the error channel.
        Assert.Contains(ops.Errors, e => e.Contains("Renderer failed"));
    }
}
