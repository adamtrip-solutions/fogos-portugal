using System.Text;
using Fogos.Domain.Incidents;
using Fogos.Worker.Jobs.Icnf;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Incidents;

/// <summary>
/// ICNF pipeline: the new-fire-from-table job creates a "3103" (Mato) incident with the -1 ICNF-only
/// sentinels, and the enrichment service merges the icnf sub-document, downloads the KML, and raises
/// IcnfEnriched whose social handler posts the first-KML / burn-area / cause messages.
/// </summary>
[Collection("fogos")]
public sealed class IcnfPipelineTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task New_fire_creates_3103_then_enrichment_merges_and_posts()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        const string id = "2026001408"; // ICNF ids are numeric (legacy ctype_digit guard)

        // ── Phase 1: table → new 3103 fire with -1 sentinels ──────────────────
        h.Icnf.Table = IncidentFixtures.IcnfTable(id);
        h.Icnf.XmlByNcco[id] = IncidentFixtures.IcnfNewFireXml(id);

        var newFireJob = new ProcessIcnfNewFireDataJob(h.Locks, NullLogger<ProcessIcnfNewFireDataJob>.Instance,
            h.Icnf.Client(), h.Ingest, h.Mongo, h.Dispatcher, h.Clock, h.Ops);
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

        // ── Phase 3: social handler posts on the icnf stream ───────────────────
        await h.DrainAsync("icnf");
        Assert.Contains(h.Twitter.Posts, p => p.Text.Contains("Area ardida disponível"));
        Assert.Contains(h.Twitter.Posts, p => p.Text.Contains("Total de área ardida"));
        Assert.Contains(h.Twitter.Posts, p => p.Text.Contains("Alerta via") && p.Text.Contains("Fogueira"));
    }
}
