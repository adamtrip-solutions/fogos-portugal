using Fogos.Worker.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Fogos.Worker.Jobs.Summaries;

/// <summary>
/// Registration entry point for the summary jobs (the <c>[jobs:summaries]</c> marker in Program.cs):
/// the twice-daily situation report (09:00 + 20:00) — Lisbon-local and single-flight. It persists the
/// report and dispatches <c>SituationReportCreated</c> for webhook delivery.
/// </summary>
public static class SummaryJobsRegistration
{
    public static IServiceCollection AddSummaryJobs(this IServiceCollection services)
    {
        services.AddQuartz(quartz =>
        {
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
