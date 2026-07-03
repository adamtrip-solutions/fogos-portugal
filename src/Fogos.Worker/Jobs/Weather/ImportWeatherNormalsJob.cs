using Fogos.Domain.Weather;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Scheduling;
using Fogos.Worker.Jobs.Weather.Parsing;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Quartz;

namespace Fogos.Worker.Jobs.Weather;

/// <summary>
/// Manually-triggered only — registered as a durable Quartz job with <b>no trigger</b> (the legacy
/// artisan one-shot <c>weather:import-normals</c>). Fetches the IPMA climate-normals pages and parses
/// the <c>allstations</c> JS literal (the non-PDF path) into <c>weather_normals</c>, upserting on
/// (stationId, period): heat = 1991-2020, cold = 1971-2000.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ImportWeatherNormalsJob(
    IHttpClientFactory httpFactory,
    MongoContext mongo,
    IOpsNotifier ops,
    ISingleFlightLock locks,
    ILogger<ImportWeatherNormalsJob> logger) : UniqueJob(locks, logger)
{
    public const string JobName = "ImportWeatherNormals";
    public const string HttpClientName = "weather-normals";

    /// <summary>Period → IPMA allstations page URL (matches the legacy command's ALLSTATIONS_URLS).</summary>
    private static readonly (string Period, string Url)[] Sources =
    [
        (WeatherNormal.PeriodHeat, "https://www.ipma.pt/pt/oclima/normais.clima/1991-2020/"),
        (WeatherNormal.PeriodCold, "https://www.ipma.pt/pt/oclima/normais.clima/1971-2000/"),
    ];

    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    /// <summary>Core logic, directly invocable in tests (bypasses the Quartz single-flight wrapper).</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var client = httpFactory.CreateClient(HttpClientName);

        foreach (var (period, url) in Sources)
        {
            string html;
            try
            {
                html = await client.GetStringAsync(url, ct);
            }
            catch (Exception ex)
            {
                await ops.ErrorAsync($"{JobName}: fetch failed for {period} ({url}): {ex.Message}", ct);
                continue;
            }

            IReadOnlyList<ParsedNormal> parsed;
            try
            {
                parsed = NormalsParser.Parse(html);
            }
            catch (Exception ex)
            {
                await ops.ErrorAsync($"{JobName}: parse failed for {period} ({url}): {ex.Message}", ct);
                continue;
            }

            if (parsed.Count == 0)
            {
                await ops.ErrorAsync($"{JobName}: no normals parsed for {period}.", ct);
                continue;
            }

            var writes = parsed.Select(n => Upsert(period, n)).ToList();
            await mongo.WeatherNormals.BulkWriteAsync(writes, cancellationToken: ct);
            logger.LogInformation("{Job}: upserted {Count} normals for {Period}", JobName, parsed.Count, period);
        }
    }

    private static UpdateOneModel<WeatherNormal> Upsert(string period, ParsedNormal n)
    {
        var filter = Builders<WeatherNormal>.Filter.And(
            Builders<WeatherNormal>.Filter.Eq(x => x.StationId, n.StationId),
            Builders<WeatherNormal>.Filter.Eq(x => x.Period, period));
        var update = Builders<WeatherNormal>.Update
            .SetOnInsert(x => x.StationId, n.StationId)
            .SetOnInsert(x => x.Period, period)
            .Set(x => x.TmaxMean, n.TmaxMean)
            .Set(x => x.TminMean, n.TminMean);
        return new UpdateOneModel<WeatherNormal>(filter, update) { IsUpsert = true };
    }
}
