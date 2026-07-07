using Fogos.Worker.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Fogos.Worker.Jobs.Alerts;

/// <summary>
/// Registration entry point for the alerts cluster (the <c>[jobs:alerts]</c> marker in Program.cs): the
/// daily 03:30 Lisbon purge of stale subscriptions and the 03:40 purge of dead Web Push devices. The
/// alert-matching handlers register themselves via the Worker assembly scan (AddEventHandlers);
/// <see cref="Fogos.Infrastructure.Options.AlertOptions"/> / <see cref="Fogos.Infrastructure.Options.WebPushOptions"/>
/// are bound in AddFogosInfrastructure.
/// </summary>
public static class AlertsJobsRegistration
{
    public static IServiceCollection AddAlertJobs(this IServiceCollection services)
    {
        services.AddQuartz(quartz =>
        {
            quartz.AddCronJob<AlertSubscriptionPurgeJob>("0 30 3 * * ?"); // daily 03:30 Lisbon
            quartz.AddCronJob<DevicePurgeJob>("0 40 3 * * ?"); // daily 03:40 Lisbon
        });

        return services;
    }
}
