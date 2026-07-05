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
/// Daily 04:21 (Lisbon). Fetches IPMA daily observations and upserts <c>weather_daily</c> on
/// (stationId, date). Unlike legacy <c>UpdateWeatherDataDaily.php</c> (insert-only, no sentinel
/// filtering) this applies the <c>-99 → null</c> fix and upserts. Port with that deliberate deviation.
/// </summary>
[DisallowConcurrentExecution]
public sealed class UpdateWeatherDataDailyJob(
    IpmaClient ipma,
    MongoContext mongo,
    WeatherFreshnessTracker freshness,
    IOpsNotifier ops,
    ISingleFlightLock locks,
    ILogger<UpdateWeatherDataDailyJob> logger) : UniqueJob(locks, logger)
{
    public const string JobName = "UpdateWeatherDataDaily";
    public static readonly TimeSpan Cadence = TimeSpan.FromDays(1);

    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    /// <summary>Core logic, directly invocable in tests (bypasses the Quartz single-flight wrapper).</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        await freshness.CheckFreshnessAsync(JobName, Cadence, ct);

        string json;
        try
        {
            json = await ipma.GetDailyObservationsAsync(ct);
        }
        catch (Exception ex)
        {
            await ops.ErrorAsync($"{JobName}: IPMA daily observations fetch failed: {ex.Message}", ct);
            return;
        }

        IReadOnlyList<DailyWeather> daily;
        try
        {
            daily = DailyObservationsParser.Parse(json);
        }
        catch (Exception ex)
        {
            await ops.ErrorAsync($"{JobName}: daily observations parse failed: {ex.Message}", ct);
            return;
        }

        if (daily.Count == 0)
        {
            await ops.ErrorAsync($"{JobName}: empty payload — no daily observations parsed.", ct);
            return;
        }

        var writes = daily.Select(Upsert).ToList();
        await mongo.WeatherDaily.BulkWriteAsync(writes, cancellationToken: ct);

        await freshness.MarkSuccessAsync(JobName, ct);
        logger.LogInformation("{Job}: upserted {Count} daily observations", JobName, daily.Count);
    }

    private static UpdateOneModel<DailyWeather> Upsert(DailyWeather d)
    {
        var filter = Builders<DailyWeather>.Filter.And(
            Builders<DailyWeather>.Filter.Eq(x => x.StationId, d.StationId),
            Builders<DailyWeather>.Filter.Eq(x => x.Date, d.Date));
        var update = Builders<DailyWeather>.Update
            .SetOnInsert(x => x.StationId, d.StationId)
            .SetOnInsert(x => x.Date, d.Date)
            .Set(x => x.TempMax, d.TempMax)
            .Set(x => x.TempMin, d.TempMin)
            .Set(x => x.TempMean, d.TempMean)
            .Set(x => x.PrecipitationMm, d.PrecipitationMm);
        return new UpdateOneModel<DailyWeather>(filter, update) { IsUpsert = true };
    }
}
