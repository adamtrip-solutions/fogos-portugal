using Fogos.Worker.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Fogos.Worker.Jobs.Summaries;

/// <summary>
/// Registration entry point for the summary jobs (the <c>[jobs:summaries]</c> marker in Program.cs):
/// the hourly active-fire summary (minute 0), the daily 09:30 recap, and the twice-daily situation
/// report (09:00 + 20:00) — all Lisbon-local and single-flight. Cadences ported from bootstrap/app.php
/// (<c>hourly</c> and <c>dailyAt 09:30</c>); the situation report is new in WP4.
/// </summary>
public static class SummaryJobsRegistration
{
    public static IServiceCollection AddSummaryJobs(this IServiceCollection services)
    {
        services.AddQuartz(quartz =>
        {
            quartz.AddCronJob<HourlySummaryJob>("0 0 * * * ?");   // top of every hour
            quartz.AddCronJob<DailySummaryJob>("0 30 9 * * ?");   // daily 09:30

            // Situation report: one job, two Lisbon-local cron triggers (the job derives its slot from the hour).
            var sitrepKey = new JobKey(nameof(SituationReportJob));
            quartz.AddJob<SituationReportJob>(o => o.WithIdentity(sitrepKey));
            quartz.AddTrigger(t => t.ForJob(sitrepKey).WithIdentity($"{nameof(SituationReportJob)}-morning")
                .WithCronSchedule("0 0 9 * * ?", x => x.InTimeZone(JobScheduleBuilder.Lisbon)));
            quartz.AddTrigger(t => t.ForJob(sitrepKey).WithIdentity($"{nameof(SituationReportJob)}-evening")
                .WithCronSchedule("0 0 20 * * ?", x => x.InTimeZone(JobScheduleBuilder.Lisbon)));
        });

        return services;
    }
}
