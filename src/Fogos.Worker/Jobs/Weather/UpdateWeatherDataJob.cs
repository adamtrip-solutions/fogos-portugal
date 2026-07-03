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
/// Hourly. Fetches IPMA hourly <c>observations.json</c> and upserts <c>weather_hourly</c> on
/// (stationId, at). Metrics carry the <c>-99 → null</c> sanitisation. Port of <c>UpdateWeatherData.php</c>.
/// </summary>
[DisallowConcurrentExecution]
public sealed class UpdateWeatherDataJob(
    IpmaClient ipma,
    MongoContext mongo,
    WeatherFreshnessTracker freshness,
    IOpsNotifier ops,
    ISingleFlightLock locks,
    ILogger<UpdateWeatherDataJob> logger) : UniqueJob(locks, logger)
{
    public const string JobName = "UpdateWeatherData";
    public static readonly TimeSpan Cadence = TimeSpan.FromHours(1);

    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    /// <summary>Core logic, directly invocable in tests (bypasses the Quartz single-flight wrapper).</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        await freshness.CheckFreshnessAsync(JobName, Cadence, ct);

        string json;
        try
        {
            json = await ipma.GetObservationsAsync(ct);
        }
        catch (Exception ex)
        {
            await ops.ErrorAsync($"{JobName}: IPMA observations fetch failed: {ex.Message}", ct);
            return;
        }

        IReadOnlyList<WeatherObservation> observations;
        try
        {
            observations = ObservationsParser.Parse(json);
        }
        catch (Exception ex)
        {
            await ops.ErrorAsync($"{JobName}: observations parse failed: {ex.Message}", ct);
            return;
        }

        if (observations.Count == 0)
        {
            await ops.ErrorAsync($"{JobName}: empty payload — no observations parsed.", ct);
            return;
        }

        var writes = observations.Select(Upsert).ToList();
        await mongo.WeatherHourly.BulkWriteAsync(writes, cancellationToken: ct);

        await freshness.MarkSuccessAsync(JobName, ct);
        logger.LogInformation("{Job}: upserted {Count} hourly observations", JobName, observations.Count);
    }

    private static UpdateOneModel<WeatherObservation> Upsert(WeatherObservation o)
    {
        var filter = Builders<WeatherObservation>.Filter.And(
            Builders<WeatherObservation>.Filter.Eq(x => x.StationId, o.StationId),
            Builders<WeatherObservation>.Filter.Eq(x => x.At, o.At));
        // $set the metrics; leave _id to Mongo on insert (ObjectId) via SetOnInsert of the keys only.
        var update = Builders<WeatherObservation>.Update
            .SetOnInsert(x => x.StationId, o.StationId)
            .SetOnInsert(x => x.At, o.At)
            .Set(x => x.Temperature, o.Temperature)
            .Set(x => x.Humidity, o.Humidity)
            .Set(x => x.WindSpeedKmh, o.WindSpeedKmh)
            .Set(x => x.WindDirection, o.WindDirection)
            .Set(x => x.PrecipitationMm, o.PrecipitationMm)
            .Set(x => x.Pressure, o.Pressure)
            .Set(x => x.Radiation, o.Radiation);
        return new UpdateOneModel<WeatherObservation>(filter, update) { IsUpsert = true };
    }
}
