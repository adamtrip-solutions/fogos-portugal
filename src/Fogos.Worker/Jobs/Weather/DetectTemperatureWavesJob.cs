using System.Globalization;
using Fogos.Domain.Time;
using Fogos.Domain.Weather;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Weather;

/// <summary>
/// Daily 05:00 (Lisbon). Runs the pure <see cref="WaveDetector"/> over <c>weather_daily</c> against
/// <c>weather_normals</c> per station: heat vs the 1991-2020 period, cold vs 1971-2000. Resets all
/// prior <c>ongoing</c> flags for each station+type, then upserts the current window on
/// (stationId, type, startDate) and pings ops (Discord) only for newly created waves
/// (legacy <c>wasRecentlyCreated</c>). Port of <c>DetectTemperatureWaves.php:70-163</c>.
/// </summary>
[DisallowConcurrentExecution]
public sealed class DetectTemperatureWavesJob(
    MongoContext mongo,
    IClock clock,
    WeatherFreshnessTracker freshness,
    IOpsNotifier ops,
    ISingleFlightLock locks,
    ILogger<DetectTemperatureWavesJob> logger) : UniqueJob(locks, logger)
{
    public const string JobName = "DetectTemperatureWaves";
    public static readonly TimeSpan Cadence = TimeSpan.FromDays(1);

    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    /// <summary>Core logic, directly invocable in tests (bypasses the Quartz single-flight wrapper).</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        await freshness.CheckFreshnessAsync(JobName, Cadence, ct);

        var today = clock.LisbonToday;
        var since = today.AddDays(-WaveDetector.LookbackDays);

        List<WeatherNormal> normals;
        try
        {
            normals = await mongo.WeatherNormals.Find(Builders<WeatherNormal>.Filter.Empty).ToListAsync(ct);
        }
        catch (Exception ex)
        {
            await ops.ErrorAsync($"{JobName}: loading normals failed: {ex.Message}", ct);
            return;
        }

        var stationNames = (await mongo.WeatherStations.Find(Builders<WeatherStation>.Filter.Empty).ToListAsync(ct))
            .ToDictionary(s => s.Id, s => s.Name);

        var created = 0;
        foreach (var group in normals.GroupBy(n => n.StationId))
        {
            var heatNormal = group.FirstOrDefault(n => n.Period == WeatherNormal.PeriodHeat);
            var coldNormal = group.FirstOrDefault(n => n.Period == WeatherNormal.PeriodCold);

            var daily = await mongo.WeatherDaily
                .Find(Builders<DailyWeather>.Filter.And(
                    Builders<DailyWeather>.Filter.Eq(x => x.StationId, group.Key),
                    Builders<DailyWeather>.Filter.Gte(x => x.Date, since)))
                .Sort(Builders<DailyWeather>.Sort.Ascending(x => x.Date))
                .ToListAsync(ct);

            if (daily.Count == 0)
                continue;

            var name = stationNames.GetValueOrDefault(group.Key, $"estação {group.Key}");

            if (heatNormal is not null)
                created += await EvaluateAsync(group.Key, WaveType.Heat, heatNormal, daily, today, name, ct);
            if (coldNormal is not null)
                created += await EvaluateAsync(group.Key, WaveType.Cold, coldNormal, daily, today, name, ct);
        }

        await freshness.MarkSuccessAsync(JobName, ct);
        logger.LogInformation("{Job}: completed for {Date}; {Created} new wave(s)", JobName, today, created);
    }

    private async Task<int> EvaluateAsync(
        int stationId,
        WaveType type,
        WeatherNormal normal,
        IReadOnlyList<DailyWeather> daily,
        DateOnly today,
        string stationName,
        CancellationToken ct)
    {
        var monthly = type == WaveType.Heat ? normal.TmaxMean : normal.TminMean;
        var window = WaveDetector.Detect(type, daily, monthly, today);

        // Reset all prior ongoing flags for this station+type before marking the current window.
        await mongo.TemperatureWaves.UpdateManyAsync(
            Builders<TemperatureWave>.Filter.And(
                Builders<TemperatureWave>.Filter.Eq(x => x.StationId, stationId),
                Builders<TemperatureWave>.Filter.Eq(x => x.Type, type),
                Builders<TemperatureWave>.Filter.Eq(x => x.Ongoing, true)),
            Builders<TemperatureWave>.Update.Set(x => x.Ongoing, false),
            cancellationToken: ct);

        if (window is null)
            return 0;

        var filter = Builders<TemperatureWave>.Filter.And(
            Builders<TemperatureWave>.Filter.Eq(x => x.StationId, stationId),
            Builders<TemperatureWave>.Filter.Eq(x => x.Type, type),
            Builders<TemperatureWave>.Filter.Eq(x => x.StartDate, window.Start));
        var update = Builders<TemperatureWave>.Update
            .SetOnInsert(x => x.StationId, stationId)
            .SetOnInsert(x => x.Type, type)
            .SetOnInsert(x => x.StartDate, window.Start)
            .Set(x => x.EndDate, window.End)
            .Set(x => x.Ongoing, window.Ongoing)
            .Set(x => x.Days, window.Days.ToList())
            .Set(x => x.UpdatedAt, clock.UtcNow);

        var result = await mongo.TemperatureWaves.UpdateOneAsync(
            filter, update, new UpdateOptions { IsUpsert = true }, ct);

        if (result.UpsertedId is null)
            return 0;

        await ops.InfoAsync(BuildNotice(stationId, stationName, type, normal.Period, window), ct);
        return 1;
    }

    private static string BuildNotice(int stationId, string stationName, WaveType type, string period, WaveWindow window)
    {
        var label = type == WaveType.Heat ? "🔥 Onda de calor detectada" : "🥶 Onda de frio detectada";
        var peak = window.PeakDeviation.ToString("+0.0;-0.0", CultureInfo.InvariantCulture);
        var normal = window.MonthNormal.ToString("0.0", CultureInfo.InvariantCulture);
        return $"{label} — {stationName} (id {stationId})\n"
               + $"Janela: {window.Start:yyyy-MM-dd} → {window.End:yyyy-MM-dd}\n"
               + $"Desvio máximo: {peak}°C | Normal mensal ({period}): {normal}°C";
    }
}
