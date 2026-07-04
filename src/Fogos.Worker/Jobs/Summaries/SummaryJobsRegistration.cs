using Fogos.Worker.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Fogos.Worker.Jobs.Summaries;

/// <summary>
/// Registration entry point for the summary jobs (the <c>[jobs:summaries]</c> marker in Program.cs):
/// the hourly active-fire summary (minute 0) and the daily 09:30 recap, both Lisbon-local and single-flight.
/// Cadences ported from bootstrap/app.php (<c>hourly</c> and <c>dailyAt 09:30</c>).
/// </summary>
public static class SummaryJobsRegistration
{
    public static IServiceCollection AddSummaryJobs(this IServiceCollection services)
    {
        services.AddQuartz(quartz =>
        {
            quartz.AddCronJob<HourlySummaryJob>("0 0 * * * ?");   // top of every hour
            quartz.AddCronJob<DailySummaryJob>("0 30 9 * * ?");   // daily 09:30
        });

        return services;
    }
}
