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

/// <summary>airplanes.live poller (offset 1 of the 3-minute plane cycle). Legacy <c>ProcessAirplanesLivePlanes</c>.</summary>
[DisallowConcurrentExecution]
public sealed class ProcessAirplanesLivePlanesJob(
    AirplanesLiveClient client,
    IOptions<FogosSourcesOptions> sources,
    AircraftReads aircraftReads,
    MongoContext mongo,
    IClock clock,
    IOpsNotifier ops,
    PlaneJobFreshness freshness,
    ILogger<ProcessAirplanesLivePlanesJob> logger)
    : ProcessAdsbPlanesJobBase(aircraftReads, mongo, clock, ops, freshness, logger)
{
    protected override string JobName => "airplaneslive-planes";

    protected override string SourceLabel => "airplaneslive";

    protected override string? BaseUrl => sources.Value.AirplanesLive.BaseUrl;

    protected override Task<string> FetchAsync(string pathAndQuery, CancellationToken ct) =>
        client.GetAsync(pathAndQuery, ct);
}
