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

        // ICNF kickoff enqueued fire creations onto the icnf stream.
        var icnf = await h.Redis.GetDatabase().StreamLengthAsync(QueueKeys.Stream("icnf"));
        Assert.True(icnf > 0);
    }

    [SkippableFact]
    public async Task Insert_seeds_one_status_observation_at_now_without_a_transition_event()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await SeedGeographyAsync(h);

        var raw = NewFireRaw("OBS1", "Em Curso");
        var outcome = await h.Ingest.IngestAsync([raw]);
        Assert.Equal(1, outcome.Created);

        // Exactly one observation, stamped at ingestion time, carrying the initial status.
        var seed = Assert.Single(await StatusRows(h, "OBS1"));
        Assert.Equal(IncidentStatusCatalog.EmCurso, seed.Code);
        Assert.Equal(h.Clock.UtcNow, seed.At);

        // The seed is an observation, not a transition: only IncidentCreated hits the stream, never IncidentStatusChanged.
        var types = (await h.Redis.GetDatabase().StreamRangeAsync(QueueKeys.Stream("default")))
            .Select(e => (string)e[RedisEventDispatcher.TypeField]!).ToList();
        Assert.Contains(nameof(IncidentCreated), types);
        Assert.DoesNotContain(nameof(IncidentStatusChanged), types);
        await h.DrainAsync("default"); // clear the shared stream
    }

    [SkippableFact]
    public async Task Witnessed_transition_appends_after_the_seed_without_duplicating_it()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await SeedGeographyAsync(h);

        await h.Ingest.IngestAsync([NewFireRaw("OBS2", "Em Curso")]);
        await h.DrainAsync("default"); // run create-side handlers, clear the stream

        // A later sweep witnesses a real transition Em Curso (5) → Em Resolução (7).
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(30);
        var outcome = await h.Ingest.IngestAsync([NewFireRaw("OBS2", "Em Resolução")]);
        Assert.Equal(1, outcome.Updated);
        await h.DrainAsync("default"); // routes IncidentStatusChanged → the history handler

        var rows = await h.Mongo.IncidentStatusHistory
            .Find(Builders<IncidentStatusChange>.Filter.Eq(x => x.IncidentId, "OBS2"))
            .Sort(Builders<IncidentStatusChange>.Sort.Ascending(x => x.At)).ToListAsync();
        Assert.Equal(2, rows.Count); // seed + transition, no dupe of the seed
        Assert.Equal(IncidentStatusCatalog.EmCurso, rows[0].Code);
        Assert.Equal(IncidentStatusCatalog.EmResolucao, rows[1].Code);
        // statusChangedAt = latest entry, non-null from birth and advanced by the transition.
        Assert.Equal(h.Clock.UtcNow, rows[1].At);
    }

    [SkippableFact]
    public async Task Aero_medical_ops_alert_fires_once_and_is_idempotent_across_redelivery()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();
        using var h = new IncidentPipelineHarness(fixture);

        // Aero-medical occurrence (naturezaCode 2409) → one Discord ops info alert.
        var aero = Fire("AERO1", IncidentStatusCatalog.EmCurso, 10, 5, 1, h.Clock.UtcNow);
        aero.NaturezaCode = NaturezaCatalog.AeroAlertCode;
        aero.Location = "Serra da Estrela";
        await h.Mongo.Incidents.InsertOneAsync(aero);
        // A plain fire in the same batch must NOT raise an ops alert.
        await h.Mongo.Incidents.InsertOneAsync(Fire("PLAIN1", IncidentStatusCatalog.EmCurso, 10, 5, 1, h.Clock.UtcNow));

        var handler = new AeroMedicalOpsHandler(h.Mongo, h.Clock, h.Processed, h.Ops);

        await handler.HandleAsync(new IncidentCreated("AERO1"), CancellationToken.None);
        await handler.HandleAsync(new IncidentCreated("PLAIN1"), CancellationToken.None);
        // At-least-once redelivery re-runs the handler on the same event → no duplicate alert.
        await handler.HandleAsync(new IncidentCreated("AERO1"), CancellationToken.None);

        var alert = Assert.Single(h.Ops.Infos);
        Assert.Contains("acidente aereo", alert);
        Assert.Contains("Serra da Estrela", alert);
    }

    [SkippableFact]
    public async Task CloseOut_leaves_first_miss_within_grace_untouched()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        // Seen 5 min ago; grace is 30 min → still inside the window on its first miss.
        await h.Mongo.Incidents.InsertOneAsync(
            Fire("FRESH_MISS", IncidentStatusCatalog.EmCurso, 5, 5, 0, h.Clock.UtcNow, lastSeenInFeedAt: h.Clock.UtcNow.AddMinutes(-5)));

        var closed = await CloseOutJob(h).CloseOutMissingAsync(new HashSet<string>(), feedFresh: true, CancellationToken.None);

        Assert.Equal(0, closed);
        var still = await Find(h, "FRESH_MISS");
        Assert.True(still.Active);
        Assert.Equal(IncidentStatusCatalog.EmCurso, still.Status.Code);
    }

    [SkippableFact]
    public async Task CloseOut_terminates_absent_past_grace_with_status13_history_and_event()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await h.Mongo.Incidents.InsertOneAsync(Fire("KEEP", IncidentStatusCatalog.EmCurso, 5, 5, 0, h.Clock.UtcNow, lastSeenInFeedAt: h.Clock.UtcNow));
        // Missing and last seen 40 min ago (> 30 min grace) → closes out.
        await h.Mongo.Incidents.InsertOneAsync(
            Fire("STALE", IncidentStatusCatalog.ChegadaAoTeatroDeOperacoes, 5, 5, 0, h.Clock.UtcNow, lastSeenInFeedAt: h.Clock.UtcNow.AddMinutes(-40)));

        var closed = await CloseOutJob(h).CloseOutMissingAsync(new HashSet<string> { "KEEP" }, feedFresh: true, CancellationToken.None);

        Assert.Equal(1, closed);
        Assert.True((await Find(h, "KEEP")).Active); // present this sweep, untouched
        var stale = await Find(h, "STALE");
        Assert.False(stale.Active);
        Assert.Equal(IncidentStatusCatalog.EncerradaSemAtualizacao, stale.Status.Code); // 13
        Assert.Equal(h.Clock.UtcNow, stale.UpdatedAt);

        // The terminal transition rides the normal status-change path: event + history row.
        var events = await h.DrainAsync("default");
        Assert.Contains(events.OfType<IncidentStatusChanged>(),
            e => e.IncidentId == "STALE" && e.PreviousCode == IncidentStatusCatalog.ChegadaAoTeatroDeOperacoes
                 && e.CurrentCode == IncidentStatusCatalog.EncerradaSemAtualizacao);
        var row = await h.Mongo.IncidentStatusHistory
            .Find(Builders<IncidentStatusChange>.Filter.Eq(x => x.IncidentId, "STALE")).FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.Equal(IncidentStatusCatalog.EncerradaSemAtualizacao, row!.Code);
    }

    [SkippableFact]
    public async Task CloseOut_skips_everything_when_feed_is_stale()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await h.Mongo.Incidents.InsertOneAsync(
            Fire("STALE", IncidentStatusCatalog.EmCurso, 5, 5, 0, h.Clock.UtcNow, lastSeenInFeedAt: h.Clock.UtcNow.AddMinutes(-40)));

        var closed = await CloseOutJob(h).CloseOutMissingAsync(new HashSet<string>(), feedFresh: false, CancellationToken.None);

        Assert.Equal(0, closed);
        Assert.True((await Find(h, "STALE")).Active); // a frozen feed cannot signal an ending
    }

    [SkippableFact]
    public async Task CloseOut_aborts_and_alerts_ops_when_candidates_exceed_fraction_cap()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        // 5 active, all absent and past grace → cap = max(3, 0.25*5=1) = 3; 5 > 3 → abort.
        for (var i = 0; i < 5; i++)
            await h.Mongo.Incidents.InsertOneAsync(
                Fire($"BULK{i}", IncidentStatusCatalog.EmCurso, 5, 5, 0, h.Clock.UtcNow, lastSeenInFeedAt: h.Clock.UtcNow.AddMinutes(-40)));

        var closed = await CloseOutJob(h).CloseOutMissingAsync(new HashSet<string>(), feedFresh: true, CancellationToken.None);

        Assert.Equal(0, closed);
        for (var i = 0; i < 5; i++)
            Assert.True((await Find(h, $"BULK{i}")).Active); // nothing closed on a suspected truncated feed
        Assert.Contains(h.Ops.Errors, m => m.Contains("truncado"));
    }

    [SkippableFact]
    public async Task CloseOut_null_last_seen_falls_back_to_created_at()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        // Legacy doc: never stamped with LastSeenInFeedAt, created 40 min ago → CreatedAt fallback closes it.
        var legacy = Fire("LEGACY", IncidentStatusCatalog.EmCurso, 5, 5, 0, h.Clock.UtcNow, lastSeenInFeedAt: null);
        legacy.CreatedAt = h.Clock.UtcNow.AddMinutes(-40);
        await h.Mongo.Incidents.InsertOneAsync(legacy);
        // Plus one within grace by CreatedAt → stays open.
        var recent = Fire("RECENT", IncidentStatusCatalog.EmCurso, 5, 5, 0, h.Clock.UtcNow, lastSeenInFeedAt: null);
        recent.CreatedAt = h.Clock.UtcNow.AddMinutes(-5);
        await h.Mongo.Incidents.InsertOneAsync(recent);

        var closed = await CloseOutJob(h).CloseOutMissingAsync(new HashSet<string>(), feedFresh: true, CancellationToken.None);

        Assert.Equal(1, closed);
        Assert.False((await Find(h, "LEGACY")).Active);
        Assert.True((await Find(h, "RECENT")).Active);
        await h.DrainAsync("default"); // clear dispatched events off the shared stream
    }

    [SkippableFact]
    public async Task Revival_from_closeout_reactivates_and_emits_status_event()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await SeedGeographyAsync(h);
        // NEW1 previously closed out (status 13, inactive); it reappears in the feed as "Em Curso".
        await h.Mongo.Incidents.InsertOneAsync(
            Fire("NEW1", IncidentStatusCatalog.EncerradaSemAtualizacao, 5, 5, 0, h.Clock.UtcNow, active: false,
                lastSeenInFeedAt: h.Clock.UtcNow.AddHours(-2)));

        var raws = await h.ArcGisSource(IncidentFixtures.FeaturePage()).FetchAsync();
        await h.Ingest.IngestAsync(raws);

        var revived = await Find(h, "NEW1");
        Assert.True(revived.Active);
        Assert.Equal(IncidentStatusCatalog.EmCurso, revived.Status.Code);
        Assert.Equal(h.Clock.UtcNow, revived.LastSeenInFeedAt);

        var events = await h.DrainAsync("default");
        Assert.Contains(events.OfType<IncidentStatusChanged>(),
            e => e.IncidentId == "NEW1" && e.PreviousCode == IncidentStatusCatalog.EncerradaSemAtualizacao
                 && e.CurrentCode == IncidentStatusCatalog.EmCurso);
    }

    [SkippableFact]
    public async Task LastSeenInFeedAt_is_bumped_for_unchanged_incidents()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await SeedGeographyAsync(h);

        var raws = await h.ArcGisSource(IncidentFixtures.FeaturePage()).FetchAsync();
        await h.Ingest.IngestAsync(raws);
        var firstSeen = (await Find(h, "NEW1")).LastSeenInFeedAt;
        Assert.Equal(h.Clock.UtcNow, firstSeen);

        // A later, byte-identical sweep: nothing changes but presence is re-stamped.
        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(10);
        var outcome = await h.Ingest.IngestAsync(raws);
        Assert.Equal(0, outcome.Updated); // genuinely unchanged
        Assert.Equal(0, outcome.Created);
        Assert.Equal(h.Clock.UtcNow, (await Find(h, "NEW1")).LastSeenInFeedAt);
        await h.DrainAsync("default"); // clear the create/change events off the shared stream
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

    private static Task<List<IncidentStatusChange>> StatusRows(IncidentPipelineHarness h, string id) =>
        h.Mongo.IncidentStatusHistory
            .Find(Builders<IncidentStatusChange>.Filter.Eq(x => x.IncidentId, id)).ToListAsync();

    private Infrastructure.Ingest.RawIncident NewFireRaw(string id, string statusLabel) =>
        new()
        {
            Id = id,
            OccurredAt = new DateTimeOffset(2026, 8, 1, 13, 0, 0, TimeSpan.Zero),
            NaturezaCode = "3101",
            Natureza = "Incêndio Florestal",
            StatusLabel = statusLabel,
            Concelho = "Ourém",
            Freguesia = "Freixianda",
            Lat = 39.66,
            Lng = -8.45,
            Resources = new Resources { Man = 10, Terrain = 4, Aerial = 1 },
        };

    private static Incident Fire(string id, int statusCode, int man, int terrain, int aerial, DateTimeOffset occurredAt,
        bool active = true, bool important = false, DateTimeOffset? lastSeenInFeedAt = null) =>
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
            LastSeenInFeedAt = lastSeenInFeedAt,
        };

    private static ProcessOcorrenciasSiteJob CloseOutJob(IncidentPipelineHarness h, IncidentPipelineOptions? options = null) =>
        new(h.Locks, NullLogger<ProcessOcorrenciasSiteJob>.Instance,
            h.ArcGisSource(IncidentFixtures.FeaturePage()), h.Ingest, h.Important,
            new IncidentFeedFreshness(h.Redis, h.Ops, h.Clock),
            h.Mongo, h.Ops, h.Dispatcher, h.Clock, Options.Create(options ?? new IncidentPipelineOptions()));
}
