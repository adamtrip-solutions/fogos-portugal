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
/// Every 15 min. Scrapes the IPMA homepage <c>result_warnings</c> JS blob, drops green warnings,
/// dedups by md5 <c>control</c> hash, and inserts new <c>weather_warnings</c>. Port of
/// <c>HandleWeatherWarnings.php</c>.
/// </summary>
[DisallowConcurrentExecution]
public sealed class HandleWeatherWarningsJob(
    IpmaClient ipma,
    MongoContext mongo,
    IClock clock,
    WeatherFreshnessTracker freshness,
    IOpsNotifier ops,
    ISingleFlightLock locks,
    ILogger<HandleWeatherWarningsJob> logger) : UniqueJob(locks, logger)
{
    public const string JobName = "HandleWeatherWarnings";
    public static readonly TimeSpan Cadence = TimeSpan.FromMinutes(15);

    protected override Task ExecuteCoreAsync(IJobExecutionContext context) => RunAsync(context.CancellationToken);

    /// <summary>Core logic, directly invocable in tests (bypasses the Quartz single-flight wrapper).</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        await freshness.CheckFreshnessAsync(JobName, Cadence, ct);

        string html;
        try
        {
            html = await ipma.GetHomepageHtmlAsync(ct);
        }
        catch (Exception ex)
        {
            await ops.ErrorAsync($"{JobName}: IPMA homepage fetch failed: {ex.Message}", ct);
            return;
        }

        IReadOnlyList<ScrapedWarning> warnings;
        try
        {
            warnings = WarningsScraper.Parse(html, clock);
        }
        catch (Exception ex)
        {
            await ops.ErrorAsync($"{JobName}: warnings scrape failed: {ex.Message}", ct);
            return;
        }

        var inserted = 0;
        foreach (var w in warnings)
        {
            var exists = await mongo.WeatherWarnings
                .Find(Builders<WeatherWarning>.Filter.Eq(x => x.Control, w.Control))
                .AnyAsync(ct);
            if (exists)
                continue;

            var warning = new WeatherWarning
            {
                AreaCode = w.AreaCode,
                AwarenessType = w.AwarenessType,
                Level = w.Level,
                StartsAt = w.StartsAt,
                EndsAt = w.EndsAt,
                Text = w.Text,
                Control = w.Control,
                CreatedAt = clock.UtcNow,
            };

            try
            {
                await mongo.WeatherWarnings.InsertOneAsync(warning, cancellationToken: ct);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                continue; // lost a race on the unique control index — already stored.
            }

            inserted++;
        }

        // A successful scrape with zero warnings is still a success (no active warnings is normal).
        await freshness.MarkSuccessAsync(JobName, ct);
        logger.LogInformation("{Job}: {Inserted} new warning(s) of {Total} scraped", JobName, inserted, warnings.Count);
    }
}
