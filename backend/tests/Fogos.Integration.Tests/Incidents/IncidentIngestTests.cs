using Fogos.Domain.Events;
using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;
using Fogos.Domain.Stats;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Queue;
using Fogos.Worker.Handlers;
using Fogos.Worker.Jobs.Incidents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Incidents;

[Collection("fogos")]
public sealed class IncidentIngestTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Full_ingest_upserts_canonical_docs_raises_events_and_runs_handlers()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await SeedGeographyAsync(h);
        await h.Mongo.Incidents.InsertOneAsync(Fire("SEEDED_STATUS", IncidentStatusCatalog.EmCurso, 30, 8, 1, h.Clock.UtcNow.AddHours(-2)));
        await h.Mongo.Incidents.InsertOneAsync(Fire("SEEDED_RES", IncidentStatusCatalog.EmCurso, 10, 5, 1, h.Clock.UtcNow.AddHours(-2)));

        var raws = await h.ArcGisSource(IncidentFixtures.FeaturePage()).FetchAsync();
        Assert.Equal(6, raws.Count);

        var outcome = await h.Ingest.IngestAsync(raws);
        Assert.Equal(4, outcome.Created);  // NEW1, FMA1, PAD1, DIRTY1
        Assert.Equal(2, outcome.Updated);  // SEEDED_STATUS, SEEDED_RES

        // Canonical mapping.
        var new1 = await Find(h, "NEW1");
        Assert.Equal(IncidentStatusCatalog.EmCurso, new1.Status.Code);
        Assert.Equal(IncidentKind.Fire, new1.Kind);
        Assert.Equal("1408", new1.Dico);
        Assert.NotNull(new1.Coordinates);
        Assert.Equal("Santarém, Ourém, Freixianda", new1.Location);

        Assert.Equal("0812", (await Find(h, "PAD1")).Dico);              // dico padding (3-digit code)
        Assert.Equal(IncidentKind.Fma, (await Find(h, "FMA1")).Kind);
        Assert.Equal(IncidentStatusCatalog.DespachoPrimeiroAlerta, (await Find(h, "DIRTY1")).Status.Code); // dirty alias → 4

        // Events on the default stream (assert before draining routes them).
        var raw = await h.Redis.GetDatabase().StreamRangeAsync(QueueKeys.Stream("default"));
        var types = raw.Select(e => (string)e[RedisEventDispatcher.TypeField]!).ToList();
        Assert.Equal(4, types.Count(t => t == nameof(IncidentCreated)));
        Assert.Contains(nameof(IncidentStatusChanged), types);
        Assert.Contains(nameof(IncidentResourcesChanged), types);

        var events = await h.DrainAsync("default");
        Assert.Contains(events.OfType<IncidentStatusChanged>(), e => e.IncidentId == "SEEDED_STATUS" && e.PreviousCode == 5 && e.CurrentCode == 7);

        // Handler side effects.
        Assert.True(await h.Mongo.IncidentHistory.CountDocumentsAsync(FilterDefinition<IncidentHistorySnapshot>.Empty) > 0);
        Assert.True(await h.Mongo.IncidentStatusHistory.CountDocumentsAsync(Builders<IncidentStatusChange>.Filter.Eq(x => x.IncidentId, "SEEDED_STATUS")) == 1);

        Assert.NotNull((await Find(h, "NEW1")).NearestWeatherStationId);

        // Delayed pushes queued (new-incident / status-change all go through the 3-min scheduler).
        var delayed = await h.Redis.GetDatabase().SortedSetLengthAsync(QueueKeys.DelayedSet);
        Assert.True(delayed > 0);
        var withScores = await h.Redis.GetDatabase().SortedSetRangeByRankWithScoresAsync(QueueKeys.DelayedSet, 0, 0);
        var dueAt = DateTimeOffset.FromUnixTimeMilliseconds((long)withScores[0].Score);
        Assert.Equal(h.Clock.UtcNow.AddMinutes(3), dueAt, TimeSpan.FromSeconds(2));

        // ICNF kickoff enqueued fire creations onto the icnf stream.
        var icnf = await h.Redis.GetDatabase().StreamLengthAsync(QueueKeys.Stream("icnf"));
        Assert.True(icnf > 0);
    }

    [SkippableFact]
    public async Task New_incident_notifications_are_idempotent_across_redelivery()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();
        using var h = new IncidentPipelineHarness(fixture);

        await h.Mongo.Incidents.InsertOneAsync(Fire("IDEMP1", IncidentStatusCatalog.EmCurso, 10, 5, 1, h.Clock.UtcNow));

        var notify = new NewIncidentNotificationsHandler(h.Mongo, h.Clock, h.FcmNotifier, h.Scheduler, h.Processed, h.Ops);

        var evt = new IncidentCreated("IDEMP1");
        var db = h.Redis.GetDatabase();

        // First delivery: one nearby data push + one delayed district "new-incident" push.
        await notify.HandleAsync(evt, CancellationToken.None);

        Assert.Equal(1, await db.SortedSetLengthAsync(QueueKeys.DelayedSet)); // new-incident
        var nearbySends = h.Fcm.Sends.Count;
        Assert.True(nearbySends > 0);

        // At-least-once redelivery re-runs the handler on the same event → no duplicates.
        await notify.HandleAsync(evt, CancellationToken.None);

        Assert.Equal(1, await db.SortedSetLengthAsync(QueueKeys.DelayedSet));
        Assert.Equal(nearbySends, h.Fcm.Sends.Count); // nearby data push not re-sent
    }

    [SkippableFact]
    public async Task CheckIsActive_flips_only_missing_incidents()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await h.Mongo.Incidents.InsertOneAsync(Fire("KEEP", IncidentStatusCatalog.EmCurso, 5, 5, 0, h.Clock.UtcNow, active: true));
        await h.Mongo.Incidents.InsertOneAsync(Fire("GONE", IncidentStatusCatalog.EmCurso, 5, 5, 0, h.Clock.UtcNow, active: true));

        var job = new ProcessOcorrenciasSiteJob(h.Locks, NullLogger<ProcessOcorrenciasSiteJob>.Instance,
            h.ArcGisSource(IncidentFixtures.FeaturePage()), h.Ingest, h.Important, new IncidentFeedFreshness(h.Redis, h.Ops, h.Clock),
            h.Mongo, h.Ops, Options.Create(new IncidentPipelineOptions()));

        var flipped = await job.ReconcileActiveAsync(new HashSet<string> { "KEEP" }, CancellationToken.None);

        Assert.Equal(1, flipped);
        Assert.True((await Find(h, "KEEP")).Active);
        var gone = await Find(h, "GONE");
        Assert.False(gone.Active);
        Assert.Equal(IncidentStatusCatalog.EmCurso, gone.Status.Code); // status untouched
    }

    [SkippableFact]
    public async Task ImportantCheck_posts_once_above_threshold_and_ages()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await h.Mongo.Incidents.InsertOneAsync(Fire("BIG", IncidentStatusCatalog.EmCurso, 5, 20, 0, h.Clock.UtcNow.AddHours(-4)));
        await h.Mongo.Incidents.InsertOneAsync(Fire("SMALL", IncidentStatusCatalog.EmCurso, 5, 10, 0, h.Clock.UtcNow.AddHours(-4)));

        var posted = await h.Important.RunAsync();
        Assert.Equal(1, posted);
        Assert.True((await Find(h, "BIG")).Important);
        Assert.False((await Find(h, "SMALL")).Important);

        Assert.Equal(0, await h.Important.RunAsync()); // no repost
    }

    [SkippableFact]
    public async Task HistoryTotal_appends_only_on_change()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await h.Mongo.Incidents.InsertOneAsync(Fire("A", IncidentStatusCatalog.EmCurso, 10, 4, 1, h.Clock.UtcNow));
        var job = new ProcessDataForHistoryTotalJob(h.Locks, NullLogger<ProcessDataForHistoryTotalJob>.Instance, h.Mongo, h.Clock);

        await job.RunAsync(CancellationToken.None);
        await job.RunAsync(CancellationToken.None); // unchanged → no append
        Assert.Equal(1, await h.Mongo.HistoryTotals.CountDocumentsAsync(FilterDefinition<HistoryTotal>.Empty));

        await h.Mongo.Incidents.UpdateOneAsync(Builders<Incident>.Filter.Eq(x => x.Id, "A"),
            Builders<Incident>.Update.Set(x => x.Resources, new Resources { Man = 40, Terrain = 4, Aerial = 1 }));
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(2);
        await job.RunAsync(CancellationToken.None);
        Assert.Equal(2, await h.Mongo.HistoryTotals.CountDocumentsAsync(FilterDefinition<HistoryTotal>.Empty));
    }

    [SkippableFact]
    public async Task Location_resolution_handles_spain_and_dico_padding()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await SeedGeographyAsync(h);

        var spain = await h.Resolver.ResolveAsync(new Infrastructure.Ingest.RawIncident { Id = "S", SpainOverride = true, Concelho = "Espanha" });
        Assert.Equal("00", spain!.Dico);
        Assert.Equal("Espanha", spain.District);

        var pad = await h.Resolver.ResolveAsync(new Infrastructure.Ingest.RawIncident { Id = "P", Concelho = "Fafe" });
        Assert.Equal("0812", pad!.Dico);
        Assert.Equal("Braga", pad.District);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private static async Task SeedGeographyAsync(IncidentPipelineHarness h)
    {
        await h.SeedConcelhoAsync("Ourém", "1408", "1408", "14");
        await h.SeedDistrictAsync("Santarém", "14");
        await h.SeedConcelhoAsync("Lisboa", "1106", "1106", "11");
        await h.SeedDistrictAsync("Lisboa", "11");
        await h.SeedConcelhoAsync("Fafe", "812", "", "8"); // empty dico → resolver pads the code
        await h.SeedDistrictAsync("Braga", "8");
        await h.SeedStationAsync(1200535, 39.65, -8.44);
    }

    private static Task<Incident> Find(IncidentPipelineHarness h, string id) =>
        h.Mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, id)).FirstAsync();

    private static Incident Fire(string id, int statusCode, int man, int terrain, int aerial, DateTimeOffset occurredAt, bool active = true, bool important = false) =>
        new()
        {
            Id = id,
            OccurredAt = occurredAt,
            Location = "Santarém, Ourém, Freixianda",
            District = "Santarém",
            Concelho = "Ourém",
            Freguesia = "Freixianda",
            Dico = "1408",
            Coordinates = GeoPoint.FromLatLng(39.6, -8.4),
            Status = IncidentStatusCatalog.FromCode(statusCode),
            Kind = IncidentKind.Fire,
            NaturezaCode = "3101",
            Natureza = "Incêndio Florestal",
            Resources = new Resources { Man = man, Terrain = terrain, Aerial = aerial },
            Active = active,
            Important = important,
            CreatedAt = occurredAt,
            UpdatedAt = occurredAt,
        };
}
