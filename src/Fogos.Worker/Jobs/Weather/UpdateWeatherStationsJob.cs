using Fogos.Domain.Time;
using Fogos.Domain.Weather;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Scheduling;
using Fogos.Infrastructure.Sources;
using Fogos.Worker.Jobs.Weather.Parsing;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Weather;

/// <summary>
/// Daily 03:21 (Lisbon). Fetches IPMA <c>stations.json</c> and upserts <c>weather_stations</c> keyed
/// on the IPMA stationId (the collection's <c>_id</c>). Port of <c>UpdateWeatherStations.php</c>.
/// </summary>
[DisallowConcurrentExecution]
public sealed class UpdateWeatherStationsJob(
    IpmaClient ipma,
    MongoContext mongo,
    IClock clock,
    WeatherFreshnessTracker freshness,
    IOpsNotifier ops,
    ISingleFlightLock locks,
    ILogger<UpdateWeatherStationsJob> logger) : UniqueJob(locks, logger)
{
    public const string JobName = "UpdateWeatherStations";
    public static readonly TimeSpan Cadence = TimeSpan.FromDays(1);

    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    /// <summary>Core logic, directly invocable in tests (bypasses the Quartz single-flight wrapper).</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        await freshness.CheckFreshnessAsync(JobName, Cadence, ct);

        string json;
        try
        {
            json = await ipma.GetStationsAsync(ct);
        }
        catch (Exception ex)
        {
            await ops.ErrorAsync($"{JobName}: IPMA stations fetch failed: {ex.Message}", ct);
            return;
        }

        IReadOnlyList<WeatherStation> stations;
        try
        {
            stations = StationsParser.Parse(json, clock);
        }
        catch (Exception ex)
        {
            await ops.ErrorAsync($"{JobName}: stations parse failed: {ex.Message}", ct);
            return;
        }

        if (stations.Count == 0)
        {
            await ops.ErrorAsync($"{JobName}: empty payload — no stations parsed.", ct);
            return;
        }

        var writes = stations
            .Select(s => new ReplaceOneModel<WeatherStation>(
                Builders<WeatherStation>.Filter.Eq(x => x.Id, s.Id), s) { IsUpsert = true })
            .ToList();
        await mongo.WeatherStations.BulkWriteAsync(writes, cancellationToken: ct);

        await freshness.MarkSuccessAsync(JobName, ct);
        logger.LogInformation("{Job}: upserted {Count} stations", JobName, stations.Count);
    }
}
