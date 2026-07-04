using System.Net;
using Fogos.Domain.Geo;
using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Sources;
using Fogos.Worker.Jobs.Firms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Firms;

[Collection("fogos")]
public sealed class FirmsProcessorTests(ContainerFixture fixture)
{
    private const string ViirsCsv = """
        country_id,latitude,longitude,bright_ti4,scan,track,acq_date,acq_time,satellite,instrument,confidence,version,bright_ti5,frp,daynight
        PRT,40.1234,-8.5678,320.5,0.5,0.4,2026-07-04,1305,N,VIIRS,n,2.0NRT,290.1,12.3,D
        PRT,40.2000,-8.6000,331.0,0.5,0.4,2026-07-04,0007,N,VIIRS,h,2.0NRT,291.0,20.0,N
        """;

    private const string ModisCsv = """
        country_id,latitude,longitude,brightness,scan,track,acq_date,acq_time,satellite,instrument,confidence,version,bright_t31,frp,daynight
        PRT,40.3000,-8.7000,315.2,1.0,1.0,2026-07-04,0930,Terra,MODIS,75,6.1NRT,295.0,8.4,D
        """;

    private MongoContext Ctx => fixture.Factory.Services.GetRequiredService<MongoContext>();

    private async Task ResetAsync()
    {
        await Ctx.Incidents.DeleteManyAsync(FilterDefinition<Incident>.Empty);
        await Ctx.Hotspots.DeleteManyAsync(FilterDefinition<Hotspots>.Empty);
    }

    /// <summary>Inactive fire older than the 72h backfill window — never fetched.</summary>
    private static Incident OldInactiveFire(string id)
    {
        var fire = Fire(id, active: false, GeoPoint.FromLatLng(40.15, -8.6));
        fire.OccurredAt = DateTimeOffset.UtcNow.AddDays(-5);
        return fire;
    }

    private static Incident Fire(string id, bool active, GeoPoint? coords, IncidentKind kind = IncidentKind.Fire) => new()
    {
        Id = id,
        OccurredAt = DateTimeOffset.UtcNow,
        Location = "Somewhere",
        Status = new IncidentStatus(5, "Em Curso"),
        Kind = kind,
        NaturezaCode = "3111",
        Active = active,
        Coordinates = coords,
    };

    private (FirmsProcessor Processor, UrlStubHandler Handler) BuildProcessor()
    {
        var handler = new UrlStubHandler(url =>
        {
            var csv = url.Contains("VIIRS_SNPP_NRT") ? ViirsCsv
                : url.Contains("MODIS_NRT") ? ModisCsv
                : "";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(csv) };
        });

        var sources = Options.Create(new FogosSourcesOptions());
        sources.Value.Firms.Key = "test-key";
        var client = new FirmsClient(new HttpClient(handler), sources);
        var processor = new FirmsProcessor(Ctx, client, new FogosClock(), NullLogger<FirmsProcessor>.Instance);
        return (processor, handler);
    }

    [SkippableFact]
    public async Task Processes_active_fire_with_coords_and_upserts_hotspots()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        await Ctx.Incidents.InsertOneAsync(Fire("F1", active: true, GeoPoint.FromLatLng(40.15, -8.6)));

        var (processor, handler) = BuildProcessor();
        var processed = await processor.ProcessAsync();

        Assert.Equal(1, processed);
        Assert.Equal(2, handler.Calls); // VIIRS + MODIS

        var doc = await Ctx.Hotspots.Find(Builders<Hotspots>.Filter.Eq(x => x.IncidentId, "F1")).FirstOrDefaultAsync();
        Assert.NotNull(doc);
        Assert.Equal(2, doc!.Viirs.Count);
        Assert.Single(doc.Modis);
        Assert.Equal("n", doc.Viirs[0].Confidence);
        Assert.Equal(315.2, doc.Modis[0].Brightness);
    }

    [SkippableFact]
    public async Task Skips_inactive_noncoord_and_nonfire_incidents()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        await Ctx.Incidents.InsertManyAsync(new[]
        {
            Fire("active-fire", active: true, GeoPoint.FromLatLng(40.15, -8.6)),
            OldInactiveFire("inactive-fire-old"), // outside the 72h backfill window
            Fire("fire-no-coords", active: true, coords: null),
            Fire("active-fma", active: true, GeoPoint.FromLatLng(40.15, -8.6), IncidentKind.Fma),
        });

        var (processor, _) = BuildProcessor();
        var processed = await processor.ProcessAsync();

        Assert.Equal(1, processed);
        Assert.Equal(1, await Ctx.Hotspots.CountDocumentsAsync(FilterDefinition<Hotspots>.Empty));
        Assert.NotNull(await Ctx.Hotspots.Find(Builders<Hotspots>.Filter.Eq(x => x.IncidentId, "active-fire")).FirstOrDefaultAsync());
    }

    [SkippableFact]
    public async Task No_active_incidents_means_no_fetch()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        var (processor, handler) = BuildProcessor();
        var processed = await processor.ProcessAsync();

        Assert.Equal(0, processed);
        Assert.Equal(0, handler.Calls);
    }

    [SkippableFact]
    public async Task Job_skips_with_single_info_when_key_missing()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();

        var ops = new RecordingOps();
        var sources = Options.Create(new FogosSourcesOptions()); // Firms.Key stays empty
        var mux = fixture.Factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var clock = fixture.Factory.Services.GetRequiredService<IClock>();
        var freshness = new JobFreshness(mux, ops, clock);

        // Processor is present but must never be reached when the key is absent.
        var client = new FirmsClient(new HttpClient(new UrlStubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))), sources);
        var processor = new FirmsProcessor(Ctx, client, new FogosClock(), NullLogger<FirmsProcessor>.Instance);

        var job = new ProcessFirmsJob(new AlwaysGrantLock(), NullLogger<ProcessFirmsJob>.Instance, processor, sources, freshness, ops);
        await job.Execute(new FakeJobContext(ProcessFirmsJob.FreshnessJob));

        Assert.Single(ops.Infos);
        Assert.Contains("FIRMS skipped", ops.Infos.Single());
        Assert.Empty(ops.Errors);
    }

    [SkippableFact]
    public async Task Backfills_recently_closed_fires_once_with_wider_day_range()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var now = DateTimeOffset.UtcNow;

        var recent = Fire("BF1", active: false, GeoPoint.FromLatLng(39.5, -8.2));
        recent.OccurredAt = now.AddHours(-30); // closed while the worker was down → dayRange ceil(1.25)+1 = 3
        var tooOld = Fire("BF_OLD", active: false, GeoPoint.FromLatLng(39.6, -8.3));
        tooOld.OccurredAt = now.AddDays(-5);   // outside the 72h window → ignored
        await Ctx.Incidents.InsertManyAsync([recent, tooOld]);

        var (processor, handler) = BuildProcessor();
        Assert.Equal(1, await processor.ProcessAsync());
        Assert.NotEmpty(handler.Requests);
        Assert.All(handler.Requests, u => Assert.EndsWith("/3", u));
        Assert.NotNull(await Ctx.Hotspots.Find(Builders<Hotspots>.Filter.Eq(x => x.IncidentId, "BF1")).FirstOrDefaultAsync());
        Assert.Null(await Ctx.Hotspots.Find(Builders<Hotspots>.Filter.Eq(x => x.IncidentId, "BF_OLD")).FirstOrDefaultAsync());

        // Second run: BF1 is covered now — nothing to process, zero requests.
        handler.Requests.Clear();
        Assert.Equal(0, await processor.ProcessAsync());
        Assert.Empty(handler.Requests);
    }
}
