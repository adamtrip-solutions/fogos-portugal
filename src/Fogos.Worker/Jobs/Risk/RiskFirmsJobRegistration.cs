using Fogos.Worker.Jobs.Firms;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Fogos.Worker.Jobs.Risk;

/// <summary>
/// Single registration entry point for the fire-risk (RCM) and FIRMS pipelines, called from
/// <c>Program.cs</c> (the <c>[jobs:risk]</c> marker). Cadences mirror ANALYSIS.md §3:
/// RCM hourly (no social) with 09:00 (today) / 18:00 (tomorrow) social runs, the daily 08:30 PS-project
/// push, and FIRMS every 15 minutes.
/// </summary>
public static class RiskFirmsJobRegistration
{
    public static IServiceCollection AddRiskAndFirmsJobs(this IServiceCollection services, IConfiguration configuration)
    {
        // ── DI services the jobs resolve ──────────────────────────────────────────────────────
        services.Configure<RiskProjectOptions>(configuration.GetSection(RiskProjectOptions.SectionName));
        services.AddSingleton<ConcelhoPolygons>();
        services.AddSingleton<RcmProcessor>();
        services.AddSingleton<Risk.JobFreshness>();
        services.AddTransient<FirmsProcessor>(); // holds a typed HttpClient — keep off the singleton graph.
        services.AddSingleton<Firms.JobFreshness>();

        services.AddQuartz(quartz =>
        {
            // ── RCM: one job, three Lisbon-local triggers carrying the social/tomorrow flags ───
            var rcmKey = new JobKey(nameof(ProcessRcmJob));
            quartz.AddJob<ProcessRcmJob>(o => o.WithIdentity(rcmKey));

            // Hourly ingest with no social output — skip 09:00/18:00 (the social runs ingest those
            // hours), avoiding a same-instant collision with the single-flight lock.
            quartz.AddTrigger(t => t
                .ForJob(rcmKey)
                .WithIdentity($"{nameof(ProcessRcmJob)}-hourly")
                .WithCronSchedule("0 0 0-8,10-17,19-23 * * ?", x => x.InTimeZone(JobScheduleBuilder.Lisbon))
                .UsingJobData(Flags(publishSocial: false, tomorrow: false)));

            // 09:00 — publish today's risk map.
            quartz.AddTrigger(t => t
                .ForJob(rcmKey)
                .WithIdentity($"{nameof(ProcessRcmJob)}-social-today")
                .WithCronSchedule("0 0 9 * * ?", x => x.InTimeZone(JobScheduleBuilder.Lisbon))
                .UsingJobData(Flags(publishSocial: true, tomorrow: false)));

            // 18:00 — publish tomorrow's risk map (legacy ProcessRCM(true, true)).
            quartz.AddTrigger(t => t
                .ForJob(rcmKey)
                .WithIdentity($"{nameof(ProcessRcmJob)}-social-tomorrow")
                .WithCronSchedule("0 0 18 * * ?", x => x.InTimeZone(JobScheduleBuilder.Lisbon))
                .UsingJobData(Flags(publishSocial: true, tomorrow: true)));

            // ── SendRiskPSProject: daily 08:30 Lisbon ─────────────────────────────────────────
            quartz.AddCronJob<SendRiskPsProjectJob>("0 30 8 * * ?");

            // ── FIRMS: every 15 minutes ───────────────────────────────────────────────────────
            quartz.AddIntervalJob<ProcessFirmsJob>(TimeSpan.FromMinutes(15));
        });

        return services;
    }

    private static JobDataMap Flags(bool publishSocial, bool tomorrow) => new()
    {
        [ProcessRcmJob.PublishSocialKey] = publishSocial,
        [ProcessRcmJob.TomorrowKey] = tomorrow,
    };
}
