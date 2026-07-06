using System.Text;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Sources;
using Fogos.Worker.Jobs.Icnf;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Incidents;

/// <summary>
/// ICNF pipeline: the new-fire-from-table job creates a "3103" (Mato) incident with the -1 ICNF-only
/// sentinels, and the enrichment service merges the icnf sub-document, downloads the KML, and raises
/// IcnfEnriched.
/// </summary>
[Collection("fogos")]
public sealed class IcnfPipelineTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task New_fire_creates_3103_then_enrichment_merges()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        const string id = "2026001408"; // ICNF ids are numeric (legacy ctype_digit guard)

        // ── Phase 1: table → new 3103 fire with -1 sentinels ──────────────────
        h.Icnf.Table = IncidentFixtures.IcnfTable(id);
        h.Icnf.XmlByNcco[id] = IncidentFixtures.IcnfNewFireXml(id, h.Clock.UtcNow.AddHours(-1));

        var newFireJob = new ProcessIcnfNewFireDataJob(h.Locks, NullLogger<ProcessIcnfNewFireDataJob>.Instance,
            h.Icnf.Client(), h.Ingest, h.Mongo, h.Dispatcher, h.Processed,
            Microsoft.Extensions.Options.Options.Create(new FogosSourcesOptions()), h.Clock, h.Ops);
        await newFireJob.RunAsync(CancellationToken.None);

        var incident = await h.Mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, id)).FirstAsync();
        Assert.Equal("3103", incident.NaturezaCode);
        Assert.Equal(IncidentKind.Fire, incident.Kind);
        Assert.Equal(-1, incident.Resources.Man);
        Assert.Equal(-1, incident.Resources.Terrain);
        Assert.Equal(-1, incident.Resources.Aerial);
        Assert.Equal(IncidentStatusCatalog.EmCurso, incident.Status.Code);
        Assert.Equal("1408", incident.Dico);

        // ── Phase 2: enrichment merges burn area / cause / KML ─────────────────
        h.Icnf.XmlByNcco[id] = IncidentFixtures.IcnfEnrichmentXml();
        h.Icnf.KmlById[id] = Encoding.UTF8.GetBytes("<kml>PERIMETER</kml>");

        var enriched = await h.Enrichment.EnrichAsync(id, id);
        Assert.NotNull(enriched);
        Assert.True(enriched!.FirstCause);
        Assert.True(enriched.FirstKml);
        Assert.True(enriched.FirstBurnArea);

        var merged = await h.Mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, id)).FirstAsync();
        Assert.Equal(12.5, merged.Icnf!.BurnArea!.Total);
        Assert.Equal("Fogueira", merged.Icnf.Cause);
        Assert.Equal("112", merged.Icnf.AlertSource);
        Assert.Contains("PERIMETER", merged.Kml);
    }

    [SkippableFact]
    public async Task Concluded_new_fire_stamps_status_history_at_the_extinction_time()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        const string id = "2026001409";

        // A fire that started 2 days ago (inside the 3-day lookback) but is already extinguished (1 day ago).
        var alertAt = h.Clock.UtcNow.AddDays(-2);
        var extinguishedAt = h.Clock.UtcNow.AddDays(-1);
        h.Icnf.Table = IncidentFixtures.IcnfConcludedTable(id, alertAt);
        h.Icnf.XmlByNcco[id] = IncidentFixtures.IcnfConcludedFireXml(id, alertAt, extinguishedAt);

        var job = new ProcessIcnfNewFireDataJob(h.Locks, NullLogger<ProcessIcnfNewFireDataJob>.Instance,
            h.Icnf.Client(), h.Ingest, h.Mongo, h.Dispatcher, h.Processed,
            Microsoft.Extensions.Options.Options.Create(new FogosSourcesOptions()), h.Clock, h.Ops);
        await job.RunAsync(CancellationToken.None);

        var incident = await h.Mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, id)).FirstAsync();
        Assert.Equal(IncidentStatusCatalog.Conclusao, incident.Status.Code); // Extinto → Conclusão (8)

        // Map-safety: the single seeded observation is stamped at the real DHFIM extinction time, not "now".
        var rows = await h.Mongo.IncidentStatusHistory
            .Find(Builders<IncidentStatusChange>.Filter.Eq(x => x.IncidentId, id)).ToListAsync();
        var seed = Assert.Single(rows);
        Assert.Equal(IncidentStatusCatalog.Conclusao, seed.Code);
        Assert.Equal(extinguishedAt, seed.At);
        Assert.NotEqual(h.Clock.UtcNow, seed.At);
        // statusChangedAt (= latest = seed) is well outside the 3h map window, so a days-dead fire won't flood the map.
        Assert.True(h.Clock.UtcNow - seed.At > TimeSpan.FromHours(3));
    }

    [SkippableFact]
    public async Task Concluded_new_fire_without_extinction_time_falls_back_to_the_alert_time()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        const string id = "2026001410";

        // Already extinguished per the table, but the XML carries no DHFIM/DATAEXTINCAO. The seed must be
        // stamped at occurredAt — never ingestion time, which would resurface a days-dead fire as freshly closed.
        var alertAt = h.Clock.UtcNow.AddDays(-2);
        h.Icnf.Table = IncidentFixtures.IcnfConcludedTable(id, alertAt);
        h.Icnf.XmlByNcco[id] = IncidentFixtures.IcnfNewFireXml(id, alertAt); // no extinction fields

        var job = new ProcessIcnfNewFireDataJob(h.Locks, NullLogger<ProcessIcnfNewFireDataJob>.Instance,
            h.Icnf.Client(), h.Ingest, h.Mongo, h.Dispatcher, h.Processed,
            Microsoft.Extensions.Options.Options.Create(new FogosSourcesOptions()), h.Clock, h.Ops);
        await job.RunAsync(CancellationToken.None);

        var seed = Assert.Single(await h.Mongo.IncidentStatusHistory
            .Find(Builders<IncidentStatusChange>.Filter.Eq(x => x.IncidentId, id)).ToListAsync());
        Assert.Equal(IncidentStatusCatalog.Conclusao, seed.Code);
        Assert.Equal(alertAt, seed.At);
        Assert.True(h.Clock.UtcNow - seed.At > TimeSpan.FromHours(3)); // still outside the 3h map window
    }

    [SkippableFact]
    public async Task Concluded_new_fire_with_future_extinction_time_is_clamped_to_now()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        const string id = "2026001411";

        // Bad feed data: DHFIM claims tomorrow. Clamped to now so the map window can't be pinned open past it.
        var alertAt = h.Clock.UtcNow.AddDays(-2);
        h.Icnf.Table = IncidentFixtures.IcnfConcludedTable(id, alertAt);
        h.Icnf.XmlByNcco[id] = IncidentFixtures.IcnfConcludedFireXml(id, alertAt, h.Clock.UtcNow.AddDays(1));

        var job = new ProcessIcnfNewFireDataJob(h.Locks, NullLogger<ProcessIcnfNewFireDataJob>.Instance,
            h.Icnf.Client(), h.Ingest, h.Mongo, h.Dispatcher, h.Processed,
            Microsoft.Extensions.Options.Options.Create(new FogosSourcesOptions()), h.Clock, h.Ops);
        await job.RunAsync(CancellationToken.None);

        var seed = Assert.Single(await h.Mongo.IncidentStatusHistory
            .Find(Builders<IncidentStatusChange>.Filter.Eq(x => x.IncidentId, id)).ToListAsync());
        Assert.Equal(h.Clock.UtcNow, seed.At);
    }

    [SkippableFact]
    public async Task Flood_guards_cap_fetches_and_never_refetch_old_occurrences()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        const string idOld = "2026009001", idFresh = "2026009002", idCapped = "2026009003";

        h.Icnf.Table = IncidentFixtures.IcnfTable(
            (idOld, h.Clock.UtcNow.AddDays(-10)),   // ruled out from DHInicio alone — no XML fetch
            (idFresh, h.Clock.UtcNow.AddHours(-2)),
            (idCapped, (DateTimeOffset?)null));      // unknown table date — must fall back to the XML
        h.Icnf.XmlByNcco[idOld] = IncidentFixtures.IcnfNewFireXml(idOld, h.Clock.UtcNow.AddDays(-10));
        h.Icnf.XmlByNcco[idFresh] = IncidentFixtures.IcnfNewFireXml(idFresh, h.Clock.UtcNow.AddHours(-2));
        h.Icnf.XmlByNcco[idCapped] = IncidentFixtures.IcnfNewFireXml(idCapped, h.Clock.UtcNow.AddHours(-2));

        var opts = Microsoft.Extensions.Options.Options.Create(new FogosSourcesOptions
        {
            Icnf = { NewFireLookbackDays = 3, MaxOccurrenceFetchesPerRun = 1 },
        });
        var job = new ProcessIcnfNewFireDataJob(h.Locks, NullLogger<ProcessIcnfNewFireDataJob>.Instance,
            h.Icnf.Client(), h.Ingest, h.Mongo, h.Dispatcher, h.Processed, opts, h.Clock, h.Ops);

        // Run 1: the old row is ruled out from the table date with NO fetch; the fresh one uses the
        // single fetch slot and is created; the unknown-date one is deferred by the cap.
        await job.RunAsync(CancellationToken.None);
        Assert.Equal([idFresh], h.Icnf.OccurrenceRequests);
        Assert.Null(await h.Mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, idOld)).FirstOrDefaultAsync());
        Assert.NotNull(await h.Mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, idFresh)).FirstOrDefaultAsync());

        // Run 2: the old occurrence stays fetch-free forever; the deferred one gets its turn.
        await job.RunAsync(CancellationToken.None);
        Assert.DoesNotContain(idOld, h.Icnf.OccurrenceRequests);
        Assert.NotNull(await h.Mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, idCapped)).FirstOrDefaultAsync());
        Assert.Null(await h.Mongo.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, idOld)).FirstOrDefaultAsync());
    }
}
