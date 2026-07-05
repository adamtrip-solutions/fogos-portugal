using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Fogos.Worker.Jobs.Planes;

/// <summary>adsb.fi poller (offset 2 of the 3-minute plane cycle). Legacy <c>ProcessAdsbfiPlanes</c>.</summary>
[DisallowConcurrentExecution]
public sealed class ProcessAdsbfiPlanesJob(
    AdsbFiClient client,
    IOptions<FogosSourcesOptions> sources,
    AircraftReads aircraftReads,
    MongoContext mongo,
    IClock clock,
    IOpsNotifier ops,
    PlaneJobFreshness freshness,
    ILogger<ProcessAdsbfiPlanesJob> logger)
    : ProcessAdsbPlanesJobBase(aircraftReads, mongo, clock, ops, freshness, logger)
{
    protected override string JobName => "adsbfi-planes";

    protected override string SourceLabel => "adsbfi";

    protected override string? BaseUrl => sources.Value.AdsbFi.BaseUrl;

    protected override Task<string> FetchAsync(string pathAndQuery, CancellationToken ct) =>
        client.GetAsync(pathAndQuery, ct);
}
