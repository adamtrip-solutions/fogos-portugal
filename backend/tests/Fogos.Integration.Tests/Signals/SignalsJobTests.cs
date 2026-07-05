using Fogos.Domain.Events;
using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;
using Fogos.Domain.Weather;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Scheduling;
using Fogos.Integration.Tests.Incidents;
using Fogos.Worker.Jobs.Signals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Signals;

/// <summary>
/// End-to-end signals job: flags an escalating fire (and records peak assets), dispatching
/// <see cref="IncidentEscalating"/> on the transition; and evaluates the 30-30-30 critical conditions
/// from the nearest-station observation. Driven by constructing the job and calling <c>RunAsync</c>.
/// </summary>
[Collection("fogos")]
public sealed class SignalsJobTests(ContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 15, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Flags_escalating_fire_records_peak_and_dispatches_event()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);

        var fire = SeedData.Incident("SIG_ESC");
        fire.Resources = new Resources { Man = 60, Terrain = 20, Aerial = 3 }; // TotalAssets = 23
        await ctx.Incidents.InsertOneAsync(fire);

        // Baseline 40 min old (assets 5), latest now (assets 23): +18, > 1.5x → escalation onset.
        await ctx.IncidentHistory.InsertManyAsync(
        [
            new IncidentHistorySnapshot { IncidentId = "SIG_ESC", At = Now.AddMinutes(-40), Man = 15, Terrain = 5, Aerial = 0 },
            new IncidentHistorySnapshot { IncidentId = "SIG_ESC", At = Now, Man = 60, Terrain = 20, Aerial = 3 },
        ]);

        var (job, redis) = BuildJob();
        await job.RunAsync(CancellationToken.None);

        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "SIG_ESC")).FirstAsync();
        Assert.NotNull(stored.Signals);
        Assert.True(stored.Signals!.Escalating);
        Assert.Equal(Now, stored.Signals.EscalationDetectedAt);
        Assert.Equal(23, stored.Signals.PeakAssets);

        var events = await SignalsTestSupport.ReadEventsAsync(redis);
        var escalating = Assert.Single(events.OfType<IncidentEscalating>(), e => e.IncidentId == "SIG_ESC");
        Assert.Equal(23, escalating.Assets);
        Assert.Equal(5, escalating.PreviousAssets);
    }

    [SkippableFact]
    public async Task Non_escalating_fire_is_not_flagged_and_raises_no_event()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);

        var fire = SeedData.Incident("SIG_FLAT");
        fire.Resources = new Resources { Man = 10, Terrain = 5, Aerial = 0 };
        await ctx.Incidents.InsertOneAsync(fire);
        await ctx.IncidentHistory.InsertManyAsync(
        [
            new IncidentHistorySnapshot { IncidentId = "SIG_FLAT", At = Now.AddMinutes(-40), Man = 10, Terrain = 5, Aerial = 0 },
            new IncidentHistorySnapshot { IncidentId = "SIG_FLAT", At = Now, Man = 11, Terrain = 5, Aerial = 0 },
        ]);

        var (job, redis) = BuildJob();
        await job.RunAsync(CancellationToken.None);

        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "SIG_FLAT")).FirstAsync();
        Assert.False(stored.Signals!.Escalating);

        var events = await SignalsTestSupport.ReadEventsAsync(redis);
        Assert.DoesNotContain(events.OfType<IncidentEscalating>(), e => e.IncidentId == "SIG_FLAT");
    }

    [SkippableFact]
    public async Task Evaluates_critical_conditions_from_nearest_station()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);

        var fire = SeedData.Incident("SIG_CRIT", nearestStation: 77);
        await ctx.Incidents.InsertOneAsync(fire);
        await ctx.WeatherStations.InsertOneAsync(new WeatherStation
        {
            Id = 77, Coordinates = GeoPoint.FromLatLng(38.7, -9.1), Name = "S77",
        });
        // Temp 35 (>30) and humidity 18 (<30): two conditions hold → critical.
        await ctx.WeatherHourly.InsertOneAsync(new WeatherObservation
        {
            StationId = 77, At = Now, Temperature = 35, Humidity = 18, WindSpeedKmh = 10,
        });

        var (job, _) = BuildJob();
        await job.RunAsync(CancellationToken.None);

        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "SIG_CRIT")).FirstAsync();
        Assert.NotNull(stored.Signals);
        Assert.True(stored.Signals!.CriticalConditions);
        Assert.Equal(Now, stored.Signals.ConditionsEvaluatedAt);
        Assert.Contains(SignalRules.TempAbove30, stored.Signals.CriticalReasons);
        Assert.Contains(SignalRules.HumidityBelow30, stored.Signals.CriticalReasons);
        Assert.DoesNotContain(SignalRules.WindAbove30, stored.Signals.CriticalReasons);
    }

    [SkippableFact]
    public async Task Dispatch_failure_does_not_persist_escalation_and_retries_next_run()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);

        var fire = SeedData.Incident("SIG_RETRY");
        fire.Resources = new Resources { Man = 60, Terrain = 20, Aerial = 3 };
        await ctx.Incidents.InsertOneAsync(fire);
        await ctx.IncidentHistory.InsertManyAsync(
        [
            new IncidentHistorySnapshot { IncidentId = "SIG_RETRY", At = Now.AddMinutes(-40), Man = 15, Terrain = 5, Aerial = 0 },
            new IncidentHistorySnapshot { IncidentId = "SIG_RETRY", At = Now, Man = 60, Terrain = 20, Aerial = 3 },
        ]);

        // First run: the dispatch throws BEFORE the flag is persisted → escalation is not recorded.
        var (failing, _) = BuildJob(new ThrowingDispatcher());
        await failing.RunAsync(CancellationToken.None);

        var afterFailure = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "SIG_RETRY")).FirstAsync();
        Assert.True(afterFailure.Signals is null || !afterFailure.Signals.Escalating);

        // Next run with a working dispatcher: because wasEscalating is still false, it re-dispatches.
        var (job, redis) = BuildJob();
        await job.RunAsync(CancellationToken.None);

        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "SIG_RETRY")).FirstAsync();
        Assert.True(stored.Signals!.Escalating);

        var events = await SignalsTestSupport.ReadEventsAsync(redis);
        Assert.Single(events.OfType<IncidentEscalating>(), e => e.IncidentId == "SIG_RETRY");
    }

    /// <summary>An event dispatcher that always throws — models a dispatch failure mid-sequence.</summary>
    private sealed class ThrowingDispatcher : IEventDispatcher
    {
        public Task DispatchAsync(IDomainEvent evt, string stream = "default", CancellationToken ct = default) =>
            throw new InvalidOperationException("dispatch failed");
    }

    private (SignalsJob Job, IConnectionMultiplexer Redis) BuildJob(IEventDispatcher? dispatcherOverride = null)
    {
        var services = fixture.Factory.Services;
        var mongo = services.GetRequiredService<MongoContext>();
        var redis = services.GetRequiredService<IConnectionMultiplexer>();
        var clock = new TestClock { UtcNow = Now };
        var lockService = new RedisSingleFlightLock(redis);
        var dispatcher = dispatcherOverride ?? new RedisEventDispatcher(redis, clock);

        var job = new SignalsJob(
            lockService, NullLogger<SignalsJob>.Instance, mongo,
            services.GetRequiredService<IncidentReads>(),
            services.GetRequiredService<WeatherReads>(),
            services.GetRequiredService<RiskReads>(),
            clock, dispatcher, Options.Create(new SignalsOptions()));

        return (job, redis);
    }
}
