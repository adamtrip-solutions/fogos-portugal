using Fogos.Worker.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace Fogos.Worker.Jobs.Photos;

/// <summary>
/// Registers the photo-moderation jobs on the shared Quartz scheduler (the <c>[jobs:photos]</c> marker in
/// <c>Program.cs</c>): the pending-moderation ops notice every 15 minutes, matching the legacy cadence.
/// </summary>
public static class PhotoJobsRegistration
{
    public static IServiceCollection AddPhotoJobs(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            q.AddCronJob<CheckPendingPhotoModerationJob>("0 0/15 * * * ?"); // every 15 minutes
        });

        return services;
    }
}
