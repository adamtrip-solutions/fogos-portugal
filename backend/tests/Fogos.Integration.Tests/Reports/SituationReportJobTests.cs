using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Reports;
using Fogos.Domain.Stats;
using Fogos.Domain.Warnings;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Scheduling;
using Fogos.Integration.Tests.Incidents;
using Fogos.Integration.Tests.Signals;
using Fogos.Worker.Handlers;
using Fogos.Worker.Jobs.Summaries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Reports;

/// <summary>
/// The situation-report job composes correct counters from seeded live data and dispatches the created
/// event; the social handler claims the report id so redelivery posts exactly once.
/// </summary>
[Collection("fogos")]
public sealed class SituationReportJobTests(ContainerFixture fixture)
{
    // 08:00 UTC = 09:00 Lisbon (summer) → morning slot.
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Job_composes_counts_and_dispatches_event()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.Incidents.InsertOneAsync(ActiveFire("SR_A", man: 30, terrain: 10, aerial: 4, escalating: true)); // assets 14
        await ctx.Incidents.InsertOneAsync(ActiveFire("SR_B", man: 5, terrain: 2, aerial: 0));                       // assets 2
        await ctx.Incidents.InsertOneAsync(ActiveFire("SR_C", man: 20, terrain: 6, aerial: 1));                      // assets 7

        await ctx.HistoryTotals.InsertOneAsync(new HistoryTotal { At = Now.AddMinutes(-1), Man = 100, Terrain = 40, Aerial = 10, Total = 150 });
        await ctx.Warnings.InsertOneAsync(new Warning { Kind = WarningKind.Manual, Message = "Aviso recente", CreatedAt = Now.AddHours(-2) });
        await ctx.Warnings.InsertOneAsync(new Warning { Kind = WarningKind.Manual, Message = "Aviso antigo", CreatedAt = Now.AddHours(-20) });

        var (job, redis) = BuildJob();
        await job.RunAsync(CancellationToken.None);

        var report = await ctx.SituationReports.Find(FilterDefinition<SituationReport>.Empty).SingleAsync();
        Assert.Equal("morning", report.Slot);
        Assert.Equal(3, report.ActiveFires);
        Assert.Equal(100, report.TotalMan);
        Assert.Equal(40, report.TotalTerrain);
        Assert.Equal(10, report.TotalAerial);
        Assert.Equal(["SR_A", "SR_C", "SR_B"], report.TopIncidentIds); // ordered by TotalAssets desc
        Assert.Contains("Em escalada: 1", report.Body);
        Assert.Contains("Avisos nas últimas 12 h: 1", report.Body);

        var events = await SignalsTestSupport.ReadEventsAsync(redis);
        var created = Assert.Single(events.OfType<SituationReportCreated>());
        Assert.Equal(report.Id, created.ReportId);
    }

    [SkippableFact]
    public async Task Rest_latest_returns_the_newest_report()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        await ctx.SituationReports.InsertOneAsync(new SituationReport { At = Now.AddHours(-11), Slot = "morning", Body = "antigo", ActiveFires = 1 });
        await ctx.SituationReports.InsertOneAsync(new SituationReport { At = Now, Slot = "evening", Body = "recente", ActiveFires = 7, TotalMan = 42 });

        var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync("/v3/reports/latest");
        response.EnsureSuccessStatusCode();
        Assert.Equal("public, max-age=300", response.Headers.CacheControl!.ToString());

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("evening", doc.RootElement.GetProperty("slot").GetString());
        Assert.Equal(7, doc.RootElement.GetProperty("activeFires").GetInt32());
        Assert.Equal(42, doc.RootElement.GetProperty("totalMan").GetInt32());
    }

    private static Incident ActiveFire(string id, int man, int terrain, int aerial, bool escalating = false)
    {
        var incident = SeedData.Incident(id, occurredAt: Now.AddHours(-1));
        incident.Resources = new Resources { Man = man, Terrain = terrain, Aerial = aerial };
        if (escalating)
            incident.Signals = new IncidentSignals { Escalating = true };
        return incident;
    }

    private (SituationReportJob Job, IConnectionMultiplexer Redis) BuildJob()
    {
        var services = fixture.Factory.Services;
        var mongo = services.GetRequiredService<MongoContext>();
        var redis = services.GetRequiredService<IConnectionMultiplexer>();
        var clock = new TestClock { UtcNow = Now };
        var job = new SituationReportJob(
            new RedisSingleFlightLock(redis), NullLogger<SituationReportJob>.Instance, mongo,
            new IncidentReads(mongo), new StatsReads(mongo), clock,
            new RedisEventDispatcher(redis, clock), new RecordingOps());
        return (job, redis);
    }

    private async Task ResetAsync()
    {
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.DeleteManyAsync(FilterDefinition<Incident>.Empty);
        await ctx.SituationReports.DeleteManyAsync(FilterDefinition<SituationReport>.Empty);
        await ctx.HistoryTotals.DeleteManyAsync(FilterDefinition<HistoryTotal>.Empty);
        await ctx.Warnings.DeleteManyAsync(FilterDefinition<Warning>.Empty);
    }
}
