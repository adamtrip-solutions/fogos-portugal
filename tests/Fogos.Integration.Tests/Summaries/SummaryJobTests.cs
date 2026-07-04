using System.Net;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Rendering;
using Fogos.Infrastructure.Scheduling;
using Fogos.Integration.Tests.Incidents;
using Fogos.Worker.Jobs.Summaries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Summaries;

/// <summary>
/// Hourly and daily summary jobs: dry-run social copy with the exact ported PT strings, the zero-active
/// behaviour, and text-only degradation when the renderer fails.
/// </summary>
[Collection("fogos")]
public sealed class SummaryJobTests(ContainerFixture fixture)
{
    private const string Woman = "\U0001F469‍"; // 👩 + ZWJ

    [SkippableFact]
    public async Task Hourly_summary_posts_active_fire_totals_text_only_when_renderer_fails()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);

        var a = SeedData.Incident("HS_A");
        a.Resources = new Resources { Man = 10, Terrain = 3, Aerial = 1 };
        var b = SeedData.Incident("HS_B");
        b.Resources = new Resources { Man = 20, Terrain = 5, Aerial = 2 };
        await ctx.Incidents.InsertManyAsync([a, b]);

        var ops = new RecordingOps();
        // 2026-08-01 13:00Z → Lisbon (UTC+1) 14:00.
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 8, 1, 13, 0, 0, TimeSpan.Zero) };
        var job = BuildHourly(ops, clock);
        await job.RunAsync(CancellationToken.None);

        var expected = $"14:00 - 2 Incêndios em curso. Meios Mobilizados:\r\n{Woman} 30\r\n🚒 8\r\n🚁 3 \r\n https://fogos.pt #FogosPT #Status";
        var telegram = ops.Captures.Single(c => c.Channel == "telegram").Payload;
        Assert.Equal(expected, telegram);
        Assert.False(telegram.StartsWith("[img]"), "renderer failed → post must be text-only");
        // Facebook is skipped entirely when there is no screenshot (legacy behaviour).
        Assert.DoesNotContain(ops.Captures, c => c.Channel == "facebook");
    }

    [SkippableFact]
    public async Task Hourly_summary_with_no_active_fires_posts_the_no_registo_notice()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);

        var ops = new RecordingOps();
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 8, 1, 13, 0, 0, TimeSpan.Zero) };
        var job = BuildHourly(ops, clock);
        await job.RunAsync(CancellationToken.None);

        var telegram = ops.Captures.Single(c => c.Channel == "telegram").Payload;
        Assert.Equal("14:00 - Sem registo de incêndios ativos.", telegram);
    }

    [SkippableFact]
    public async Task Daily_summary_reports_yesterdays_ignitions_and_peak_means()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);

        // Yesterday = 2026-08-01 (Lisbon). Two ignitions in the window.
        var occurred = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.FromHours(1));
        await ctx.Incidents.InsertManyAsync(
        [
            SeedData.Incident("DS_A", occurredAt: occurred),
            SeedData.Incident("DS_B", occurredAt: occurred),
        ]);
        await ctx.IncidentHistory.InsertManyAsync(
        [
            new IncidentHistorySnapshot { IncidentId = "DS_A", At = occurred, Man = 10, Terrain = 3, Aerial = 1 },
            new IncidentHistorySnapshot { IncidentId = "DS_A", At = occurred.AddHours(1), Man = 30, Terrain = 8, Aerial = 2 },
            new IncidentHistorySnapshot { IncidentId = "DS_B", At = occurred, Man = 50, Terrain = 10, Aerial = 5 },
        ]);

        var ops = new RecordingOps();
        // 2026-08-02 08:00Z → Lisbon 09:00; LisbonToday = 2026-08-02, yesterday = 2026-08-01.
        var clock = new TestClock { UtcNow = new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero) };
        var job = BuildDaily(ops, clock);
        await job.RunAsync(CancellationToken.None);

        // maxMan = 30 + 50 = 80; maxCars = 8 + 10 = 18; maxPlanes = 2 + 5 = 7; burn area 0.
        var expected = "ℹ Resumo diário de ontem 01-08-2026:\r\n - Total de ignições: 2 \r\n - Operacionais Mobilizados: 80 \r\n - Veiculos Mobilizados: 18 \r\n - Missões com Meios Aéreos: 7 \r\n - Total Área Ardida contabilizada: 0 ha ℹ";
        var telegram = ops.Captures.Single(c => c.Channel == "telegram").Payload;
        Assert.Equal(expected, telegram);

        var channels = ops.Captures.Select(c => c.Channel).ToHashSet();
        Assert.Superset(new HashSet<string> { "twitter", "facebook", "telegram" }, channels);
    }

    private HourlySummaryJob BuildHourly(RecordingOps ops, TestClock clock)
    {
        var services = fixture.Factory.Services;
        var mongo = services.GetRequiredService<MongoContext>();
        var lockService = new RedisSingleFlightLock(services.GetRequiredService<IConnectionMultiplexer>());
        var (twitter, telegram, facebook) = Publishers(ops);

        var failingRenderer = new RendererClient(
            new StubHttpClientFactory(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))),
            Options.Create(new RendererOptions { Url = "http://renderer.invalid", Retries = 1, RetryBaseDelay = TimeSpan.Zero, MinBytes = 1 }),
            ops, NullLogger<RendererClient>.Instance);

        return new HourlySummaryJob(lockService, NullLogger<HourlySummaryJob>.Instance, mongo, clock,
            twitter, telegram, facebook, failingRenderer, ops);
    }

    private DailySummaryJob BuildDaily(RecordingOps ops, TestClock clock)
    {
        var services = fixture.Factory.Services;
        var mongo = services.GetRequiredService<MongoContext>();
        var lockService = new RedisSingleFlightLock(services.GetRequiredService<IConnectionMultiplexer>());
        var (twitter, telegram, facebook) = Publishers(ops);

        return new DailySummaryJob(lockService, NullLogger<DailySummaryJob>.Instance, mongo,
            services.GetRequiredService<IncidentReads>(), services.GetRequiredService<StatsReads>(), clock,
            twitter, telegram, facebook, ops);
    }

    private static (TwitterPublisher, TelegramPublisher, FacebookPublisher) Publishers(RecordingOps ops)
    {
        var publishing = Options.Create(new PublishingOptions()); // DryRun defaults
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(_ => new HttpResponseMessage()));
        return (
            new TwitterPublisher(factory, publishing, Options.Create(new TwitterOptions()), ops, NullLogger<TwitterPublisher>.Instance),
            new TelegramPublisher(factory, publishing, Options.Create(new TelegramOptions()), ops, NullLogger<TelegramPublisher>.Instance),
            new FacebookPublisher(factory, publishing, Options.Create(new FacebookOptions()), ops, NullLogger<FacebookPublisher>.Instance));
    }
}
