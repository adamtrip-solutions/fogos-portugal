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
        var processor = new FirmsProcessor(Ctx, client, NullLogger<FirmsProcessor>.Instance);
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
            Fire("inactive-fire", active: false, GeoPoint.FromLatLng(40.15, -8.6)),
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
        var processor = new FirmsProcessor(Ctx, client, NullLogger<FirmsProcessor>.Instance);

        var job = new ProcessFirmsJob(new AlwaysGrantLock(), NullLogger<ProcessFirmsJob>.Instance, processor, sources, freshness, ops);
        await job.Execute(new FakeJobContext(ProcessFirmsJob.FreshnessJob));

        Assert.Single(ops.Infos);
        Assert.Contains("FIRMS skipped", ops.Infos.Single());
        Assert.Empty(ops.Errors);
    }
}
