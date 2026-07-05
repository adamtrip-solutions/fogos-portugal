using System.Globalization;
using Fogos.Infrastructure.Ingest;
using Fogos.Infrastructure.Options;
using Fogos.Worker.Handlers;
using Fogos.Worker.Jobs.Icnf;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;

namespace Fogos.Worker.Jobs.Incidents;

/// <summary>
/// Registration entry point for the incident cluster (the <c>[jobs:incidents]</c> marker in Program.cs):
/// the ingest services + selectable source, the ICNF enrichment service, and the
/// Quartz jobs with cadences ported from bootstrap/app.php (ArcGIS 5 min, HistoryTotal 2 min, ICNF table
/// 5 min, UpdateICNFData buckets 0–6 on their exact crons). Event handlers register themselves via the
/// Worker assembly scan (AddEventHandlers).
/// </summary>
public static class IncidentJobsRegistration
{
    public static IServiceCollection AddIncidentJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IncidentPipelineOptions>(configuration.GetSection(IncidentPipelineOptions.SectionName));

        // ── Ingest services ───────────────────────────────────────────────────────────────────
        services.AddScoped<LocationResolver>();
        services.AddScoped<IncidentIngestService>();
        services.AddScoped<ImportantFireChecker>();
        services.AddScoped<IcnfEnrichmentService>();
        services.AddSingleton<IncidentFeedFreshness>();

        // ── Sources: ArcGIS primary, ANEPC fallback (selectable via Incidents:Source) ──────────
        services.AddScoped<ArcGisOcorrenciasSource>();
        services.AddHttpClient(AnepcApiSource.HttpClientName);
        services.AddScoped<AnepcApiSource>();
        services.AddScoped<IIncidentSource>(sp =>
        {
            var source = sp.GetRequiredService<IOptions<IncidentPipelineOptions>>().Value.Source;
            return string.Equals(source, "anepc", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<AnepcApiSource>()
                : sp.GetRequiredService<ArcGisOcorrenciasSource>();
        });

        // ── Quartz jobs (Lisbon-local cadences from bootstrap/app.php) ─────────────────────────
        services.AddQuartz(quartz =>
        {
            quartz.AddIntervalJob<ProcessOcorrenciasSiteJob>(TimeSpan.FromMinutes(5));
            quartz.AddIntervalJob<ProcessDataForHistoryTotalJob>(TimeSpan.FromMinutes(2));
            quartz.AddIntervalJob<ProcessIcnfNewFireDataJob>(TimeSpan.FromMinutes(5));

            // UpdateICNFData: one durable job, one trigger per age bucket carrying the bucket index.
            var icnfKey = new JobKey(nameof(UpdateIcnfDataJob));
            quartz.AddJob<UpdateIcnfDataJob>(o => o.WithIdentity(icnfKey).StoreDurably());

            foreach (var (bucket, cron) in IcnfBucketCrons)
            {
                quartz.AddTrigger(t => t
                    .ForJob(icnfKey)
                    .WithIdentity($"{nameof(UpdateIcnfDataJob)}-bucket-{bucket}")
                    .WithCronSchedule(cron, x => x.InTimeZone(JobScheduleBuilder.Lisbon))
                    .UsingJobData(UpdateIcnfDataJob.BucketKey, bucket.ToString(CultureInfo.InvariantCulture)));
            }
        });

        return services;
    }

    /// <summary>Exact bucket cadences ported from bootstrap/app.php (Laravel cron → Quartz 6-field).</summary>
    public static readonly IReadOnlyList<(int Bucket, string Cron)> IcnfBucketCrons =
    [
        (0, "0 0 0/4 * * ?"),      // everyFourHours
        (1, "0 0 1,13 * * ?"),     // twiceDaily (01:00, 13:00)
        (2, "0 0 6 * * ?"),        // dailyAt 06:00
        (3, "0 0 2 1/2 * ?"),      // 0 2 */2 * *  (every 2nd day-of-month at 02:00)
        (4, "0 0 3 ? * MON,FRI"),  // 0 3 * * 1,5
        (5, "0 0 3 ? * MON,FRI"),  // 0 3 * * 1,5 (duplicate in legacy)
        (6, "0 0 3 ? * WED"),      // 0 3 * * 3
    ];
}
