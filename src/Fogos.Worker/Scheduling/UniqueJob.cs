using Fogos.Infrastructure.Scheduling;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Fogos.Worker.Scheduling;

/// <summary>
/// Base for jobs that must run at most once across the worker fleet at a time (the legacy
/// <c>ShouldBeUnique</c> semantics). Acquires a Redis single-flight lock keyed on the job before
/// running <see cref="ExecuteCoreAsync"/>; if the lock is held, the run is skipped. Plain jobs that
/// don't need this just implement <see cref="IJob"/> directly.
/// </summary>
public abstract class UniqueJob(ISingleFlightLock lockService, ILogger logger) : IJob
{
    /// <summary>Lock lease; long enough to outlast the job, short enough to self-heal after a crash.</summary>
    protected virtual TimeSpan LockTtl => TimeSpan.FromMinutes(10);

    public async Task Execute(IJobExecutionContext context)
    {
        var key = context.JobDetail.Key.ToString();
        var token = await lockService.TryAcquireAsync(key, LockTtl, context.CancellationToken);
        if (token is null)
        {
            logger.LogInformation("Skipping {Job}: another run holds the single-flight lock", key);
            return;
        }

        try
        {
            await ExecuteCoreAsync(context);
        }
        finally
        {
            await lockService.ReleaseAsync(key, token, CancellationToken.None);
        }
    }

    protected abstract Task ExecuteCoreAsync(IJobExecutionContext context);
}
