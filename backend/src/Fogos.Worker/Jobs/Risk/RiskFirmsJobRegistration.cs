using Fogos.Worker.Jobs.Firms;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Fogos.Worker.Jobs.Risk;

/// <summary>
/// Single registration entry point for the fire-risk (RCM) and FIRMS pipelines, called from
/// <c>Program.cs</c> (the <c>[jobs:risk]</c> marker). Cadences mirror ANALYSIS.md §3:
/// RCM hourly (ingest only) and FIRMS every 15 minutes.
/// </summary>
public static class RiskFirmsJobRegistration
{
    public static IServiceCollection AddRiskAndFirmsJobs(this IServiceCollection services, IConfiguration configuration)
    {
        // ── DI services the jobs resolve ──────────────────────────────────────────────────────
        services.AddSingleton<ConcelhoPolygons>();
        services.AddSingleton<RcmProcessor>();
        services.AddSingleton<Risk.JobFreshness>();
        services.AddTransient<FirmsProcessor>(); // holds a typed HttpClient — keep off the singleton graph.
        services.AddSingleton<Firms.JobFreshness>();

        services.AddQuartz(quartz =>
        {
            // ── RCM: hourly ingest (feeds rcm_daily/rcm_geojson + RcmProcessed for risk alerts) ─
            quartz.AddIntervalJob<ProcessRcmJob>(TimeSpan.FromHours(1));

            // ── FIRMS: every 15 minutes ───────────────────────────────────────────────────────
            quartz.AddIntervalJob<ProcessFirmsJob>(TimeSpan.FromMinutes(15));
        });

        return services;
    }
}
