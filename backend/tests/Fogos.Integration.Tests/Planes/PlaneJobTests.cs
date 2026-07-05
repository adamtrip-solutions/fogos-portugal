using System.Net;
using Fogos.Domain.Aircraft;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Sources;
using Fogos.Worker.Jobs.Planes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Planes;

/// <summary>
/// Container-backed tests for the three plane pollers: FR24 gates + first-sighting dedup + credits,
/// and the ADS-B append + consecutive-duplicate skip. Each test gets its own Mongo database and a
/// flushed Redis so the credit meter / freshness state never leaks.
/// </summary>
[Collection("fogos")]
public sealed class PlaneJobTests(ContainerFixture fixture)
{
    private static readonly DateTimeOffset LisbonMidday = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LisbonNight = new(2026, 6, 15, 2, 0, 0, TimeSpan.Zero);

    // ── FR24 ───────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Fr24_happy_path_appends_positions_and_records_credits()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        var mongo = NewMongo();
        await SeedAerialFireAsync(mongo);
        await SeedFleetAsync(mongo, notify: true);

        await using var redis = await ConnectRedisAsync();
        var clock = new FakeClock(LisbonMidday);
        var ctx = BuildFr24(mongo, redis, clock, RespondWith(PlaneFixtures.Fr24TwoAircraft));

        await ctx.Job.Execute(new FakeJobExecutionContext(CancellationToken.None));

        Assert.Equal(1, ctx.Handler.Attempts);
        Assert.Equal(2, await CountBySourceAsync(mongo, "fr24"));

        // Credits recorded via the legacy 2 + rows×0.04 model → ceil(2.08) = 3.
        Assert.Equal(3, await ctx.Meter.CurrentAsync());
    }

    [SkippableFact]
    public async Task Fr24_dry_run_mode_runs_gates_but_makes_no_paid_call()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        var mongo = NewMongo();
        await SeedAerialFireAsync(mongo);
        await SeedFleetAsync(mongo, notify: true);

        await using var redis = await ConnectRedisAsync();
        var clock = new FakeClock(LisbonMidday);
        // Every gate green, but the spend gate is DryRun → no HTTP call, no positions, no credits spent.
        var ctx = BuildFr24(mongo, redis, clock, RespondWith(PlaneFixtures.Fr24TwoAircraft),
            fr24Mode: PublisherMode.DryRun);

        await ctx.Job.Execute(new FakeJobExecutionContext(CancellationToken.None));

        Assert.Equal(0, ctx.Handler.Attempts);
        Assert.Equal(0, await CountBySourceAsync(mongo, "fr24"));
        Assert.Equal(0, await ctx.Meter.CurrentAsync());
    }

    [SkippableFact]
    public async Task Fr24_outside_daylight_window_makes_no_api_call()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        var mongo = NewMongo();
        await SeedAerialFireAsync(mongo);
        await SeedFleetAsync(mongo, notify: true);

        await using var redis = await ConnectRedisAsync();
        var ctx = BuildFr24(mongo, redis, new FakeClock(LisbonNight), RespondWith(PlaneFixtures.Fr24TwoAircraft));

        await ctx.Job.Execute(new FakeJobExecutionContext(CancellationToken.None));

        Assert.Equal(0, ctx.Handler.Attempts);
        Assert.Equal(0, await CountBySourceAsync(mongo, "fr24"));
    }

    [SkippableFact]
    public async Task Fr24_without_active_aerial_incident_makes_no_api_call()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        var mongo = NewMongo();
        await SeedFleetAsync(mongo, notify: true); // fleet present, but no aerial incident seeded

        await using var redis = await ConnectRedisAsync();
        var ctx = BuildFr24(mongo, redis, new FakeClock(LisbonMidday), RespondWith(PlaneFixtures.Fr24TwoAircraft));

        await ctx.Job.Execute(new FakeJobExecutionContext(CancellationToken.None));

        Assert.Equal(0, ctx.Handler.Attempts);
    }

    [SkippableFact]
    public async Task Fr24_credit_guard_tripped_makes_no_api_call()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        var mongo = NewMongo();
        await SeedAerialFireAsync(mongo);
        await SeedFleetAsync(mongo, notify: true);

        await using var redis = await ConnectRedisAsync();
        var clock = new FakeClock(LisbonMidday);
        // Budget 20 → guard at 19; pre-spend 19 so the guard is already tripped.
        var ctx = BuildFr24(mongo, redis, clock, RespondWith(PlaneFixtures.Fr24TwoAircraft), monthlyBudget: 20);
        for (var i = 0; i < 19; i++)
            await ctx.Meter.TryConsumeAsync();

        await ctx.Job.Execute(new FakeJobExecutionContext(CancellationToken.None));

        Assert.Equal(0, ctx.Handler.Attempts);
        Assert.Equal(19, await ctx.Meter.CurrentAsync()); // untouched by the gated run
    }

    [SkippableFact]
    public async Task Fr24_with_key_but_no_budget_refuses_to_spend()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        var mongo = NewMongo();
        await SeedAerialFireAsync(mongo);
        await SeedFleetAsync(mongo, notify: true);

        await using var redis = await ConnectRedisAsync();
        var clock = new FakeClock(LisbonMidday);
        // Key configured (BuildFr24 always sets one) but budget unset → fail closed, no API call.
        var ops = new RecordingOps();
        var ctx = BuildFr24(mongo, redis, clock, RespondWith(PlaneFixtures.Fr24TwoAircraft), monthlyBudget: 0, ops: ops);

        await ctx.Job.Execute(new FakeJobExecutionContext(CancellationToken.None));

        Assert.Equal(0, ctx.Handler.Attempts);
        Assert.Equal(0, await CountBySourceAsync(mongo, "fr24"));
        Assert.Equal(0, await ctx.Meter.CurrentAsync());

        // A single ops warning is emitted (NoteOnce), not one per run.
        await ctx.Job.Execute(new FakeJobExecutionContext(CancellationToken.None));
        var refusal = Assert.Single(ops.Infos, m => m.Contains("refusing to spend"));
        Assert.Contains("no monthly budget", refusal);

        // HasBudgetAsync fails closed directly too.
        Assert.False(await ctx.Meter.HasBudgetAsync());
    }

    // ── ADS-B (adsb.fi / airplanes.live) ─────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Adsbfi_appends_positions_with_its_source_label()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        var mongo = NewMongo();
        await SeedFleetAsync(mongo, notify: false);

        await using var redis = await ConnectRedisAsync();
        var handler = new StubHttpMessageHandler(RespondWith(PlaneFixtures.AdsbTwoAircraftPlusStale));
        var job = BuildAdsbfi(mongo, redis, new FakeClock(LisbonMidday), handler);

        await job.Execute(new FakeJobExecutionContext(CancellationToken.None));

        Assert.Equal(1, handler.Attempts);
        Assert.Equal(2, await CountBySourceAsync(mongo, "adsbfi")); // stale (seen_pos 720) dropped
        Assert.Equal(0, await CountBySourceAsync(mongo, "fr24"));
    }

    [SkippableFact]
    public async Task AirplanesLive_labels_positions_with_its_own_source()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        var mongo = NewMongo();
        await SeedFleetAsync(mongo, notify: false);

        await using var redis = await ConnectRedisAsync();
        var handler = new StubHttpMessageHandler(RespondWith(PlaneFixtures.AdsbTwoAircraftPlusStale));
        var job = BuildAirplanesLive(mongo, redis, new FakeClock(LisbonMidday), handler);

        await job.Execute(new FakeJobExecutionContext(CancellationToken.None));

        Assert.Equal(2, await CountBySourceAsync(mongo, "airplaneslive"));
    }

    [SkippableFact]
    public async Task Adsbfi_skips_consecutive_duplicate_sample()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        var mongo = NewMongo();
        await SeedFleetAsync(mongo, notify: false);

        await using var redis = await ConnectRedisAsync();
        var clock = new FakeClock(LisbonMidday); // fixed clock ⇒ identical coords + same minute on both runs
        var handler = new StubHttpMessageHandler(RespondWith(PlaneFixtures.AdsbTwoAircraftPlusStale));
        var job = BuildAdsbfi(mongo, redis, clock, handler);

        await job.Execute(new FakeJobExecutionContext(CancellationToken.None));
        await job.Execute(new FakeJobExecutionContext(CancellationToken.None));

        // Second run's samples are exact repeats (same icao + coords + minute) → skipped.
        Assert.Equal(2, await CountBySourceAsync(mongo, "adsbfi"));
    }

    // ── builders / helpers ───────────────────────────────────────────────────────────────────────

    private sealed record Fr24Context(
        ProcessFr24PlanesJob Job,
        StubHttpMessageHandler Handler,
        Fr24CreditMeter Meter);

    private static Func<int, HttpResponseMessage> RespondWith(string json) =>
        _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };

    private Fr24Context BuildFr24(
        MongoContext mongo,
        IConnectionMultiplexer redis,
        FakeClock clock,
        Func<int, HttpResponseMessage> responder,
        int monthlyBudget = 100_000, // fail-closed: a real budget is required for the job to spend at all
        PublisherMode fr24Mode = PublisherMode.On,
        RecordingOps? ops = null)
    {
        var sources = Options.Create(new FogosSourcesOptions
        {
            Fr24 = new Fr24Options { ApiKey = "test-key", MonthlyBudget = monthlyBudget, Mode = fr24Mode },
        });

        var handler = new StubHttpMessageHandler(responder);
        var fr24Client = new Fr24Client(new HttpClient(handler), sources);
        var meter = new Fr24CreditMeter(redis, clock, sources);
        ops ??= new RecordingOps();
        var freshness = new PlaneJobFreshness(redis, clock, ops);

        var job = new ProcessFr24PlanesJob(
            fr24Client, meter, new AircraftReads(mongo), mongo, clock, ops,
            sources, freshness, NullLogger<ProcessFr24PlanesJob>.Instance);

        return new Fr24Context(job, handler, meter);
    }

    private static ProcessAdsbfiPlanesJob BuildAdsbfi(MongoContext mongo, IConnectionMultiplexer redis, FakeClock clock, StubHttpMessageHandler handler)
    {
        var sources = Options.Create(new FogosSourcesOptions
        {
            AdsbFi = new PlaneSourceOptions { BaseUrl = "https://opendata.adsb.fi/api/v2" },
        });
        var ops = new RecordingOps();
        var client = new AdsbFiClient(new HttpClient(handler), sources);
        return new ProcessAdsbfiPlanesJob(
            client, sources, new AircraftReads(mongo), mongo, clock, ops,
            new PlaneJobFreshness(redis, clock, ops), NullLogger<ProcessAdsbfiPlanesJob>.Instance);
    }

    private static ProcessAirplanesLivePlanesJob BuildAirplanesLive(MongoContext mongo, IConnectionMultiplexer redis, FakeClock clock, StubHttpMessageHandler handler)
    {
        var sources = Options.Create(new FogosSourcesOptions
        {
            AirplanesLive = new PlaneSourceOptions { BaseUrl = "https://api.airplanes.live/v2" },
        });
        var ops = new RecordingOps();
        var client = new AirplanesLiveClient(new HttpClient(handler), sources);
        return new ProcessAirplanesLivePlanesJob(
            client, sources, new AircraftReads(mongo), mongo, clock, ops,
            new PlaneJobFreshness(redis, clock, ops), NullLogger<ProcessAirplanesLivePlanesJob>.Instance);
    }

    private MongoContext NewMongo() =>
        new(new MongoClient(fixture.MongoConnectionString),
            Options.Create(new MongoOptions
            {
                ConnectionString = fixture.MongoConnectionString,
                Database = "planes_test_" + Guid.NewGuid().ToString("N")[..8],
            }));

    private async Task<ConnectionMultiplexer> ConnectRedisAsync() =>
        await ConnectionMultiplexer.ConnectAsync(fixture.RedisConnectionString);

    private static async Task SeedAerialFireAsync(MongoContext mongo) =>
        await mongo.Incidents.InsertOneAsync(SeedData.Incident("inc-aerial"));

    private static async Task SeedFleetAsync(MongoContext mongo, bool notify)
    {
        await mongo.TrackedAircraft.InsertManyAsync(new[]
        {
            new TrackedAircraft
            {
                Icao = PlaneFixtures.Hex1, Registration = PlaneFixtures.Reg1,
                Name = "Bombeiros 01", Type = "AT-802", Base = "Viseu", Notify = notify, Active = true,
            },
            new TrackedAircraft
            {
                Icao = PlaneFixtures.Hex2, Registration = PlaneFixtures.Reg2,
                Name = "Bombeiros 02", Type = "EC-145", Base = "Loulé", Notify = notify, Active = true,
            },
        });
    }

    private static async Task<long> CountBySourceAsync(MongoContext mongo, string source) =>
        await mongo.FlightPositions.CountDocumentsAsync(Builders<FlightPosition>.Filter.Eq(x => x.Source, source));
}
