using Fogos.Infrastructure.Options;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Fogos.Worker.Jobs.Signals;

/// <summary>
/// Registration entry point for the signals cluster (the <c>[jobs:signals]</c> marker in Program.cs):
/// binds <see cref="SignalsOptions"/> and schedules <see cref="SignalsJob"/> on a 2-minute interval.
/// The <c>RekindleHandler</c> and <c>EscalationPushHandler</c> register themselves via the Worker
/// assembly scan (AddEventHandlers).
/// </summary>
public static class SignalsJobsRegistration
{
    public static IServiceCollection AddSignalsJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SignalsOptions>(configuration.GetSection(SignalsOptions.SectionName));

        services.AddQuartz(quartz =>
        {
            quartz.AddIntervalJob<SignalsJob>(TimeSpan.FromMinutes(2));
            quartz.AddIntervalJob<ClusterJob>(TimeSpan.FromMinutes(5));
        });

        return services;
    }
}
