using Fogos.Worker.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Fogos.Worker.Jobs.Weather;

/// <summary>
/// Registers the weather ingestion/detection jobs on the shared Quartz scheduler with their
/// verbatim legacy cadences (Lisbon timezone), plus the freshness tracker they depend on. Called
/// from the <c>[jobs:weather]</c> marker in <c>Program.cs</c>.
/// </summary>
public static class WeatherJobsRegistration
{
    public static IServiceCollection AddWeatherJobs(this IServiceCollection services)
    {
        services.AddSingleton<WeatherFreshnessTracker>();
        services.AddHttpClient(ImportWeatherNormalsJob.HttpClientName);

        services.AddQuartz(q =>
        {
            // Cadences verbatim from ANALYSIS.md §3 (all Lisbon-local).
            q.AddCronJob<UpdateWeatherStationsJob>("0 21 3 * * ?");    // daily 03:21
            q.AddCronJob<UpdateWeatherDataJob>("0 0 * * * ?");        // hourly (top of hour)
            q.AddCronJob<UpdateWeatherDataDailyJob>("0 21 4 * * ?");   // daily 04:21
            q.AddCronJob<DetectTemperatureWavesJob>("0 0 5 * * ?");    // daily 05:00
            q.AddCronJob<HandleWeatherWarningsJob>("0 0/15 * * * ?");  // every 15 minutes

            // ImportWeatherNormals is manual-only: a durable job with no trigger (legacy artisan one-shot).
            q.AddJob<ImportWeatherNormalsJob>(o => o
                .WithIdentity(new JobKey(nameof(ImportWeatherNormalsJob)))
                .StoreDurably());
        });

        return services;
    }
}
