using Fogos.Infrastructure.Options;
using Fogos.Worker.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Fogos.Worker.Jobs.Planes;

/// <summary>
/// Registers the three aircraft-tracking pollers on the Quartz scheduler and their shared freshness
/// helper. The pollers fire every 3 minutes on staggered minute offsets 0/1/2 (Lisbon time) so the
/// providers are never hit simultaneously — matching the legacy scheduler layout. Also schedules the
/// aircraft↔incident association job (every 2 minutes) and binds its thresholds.
/// </summary>
public static class PlaneJobRegistration
{
    public static IServiceCollection AddPlaneJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<PlaneJobFreshness>();
        services.Configure<AircraftAssociationOptions>(configuration.GetSection(AircraftAssociationOptions.SectionName));

        services.AddQuartz(quartz =>
        {
            // Quartz cron fields: sec min hour day-of-month month day-of-week.
            quartz.AddCronJob<ProcessFr24PlanesJob>("0 0/3 * * * ?");        // offset 0
            quartz.AddCronJob<ProcessAirplanesLivePlanesJob>("0 1/3 * * * ?"); // offset 1
            quartz.AddCronJob<ProcessAdsbfiPlanesJob>("0 2/3 * * * ?");      // offset 2
            quartz.AddIntervalJob<AircraftAssociationJob>(TimeSpan.FromMinutes(2));
        });

        return services;
    }
}
