using Quartz;

namespace Fogos.Worker.Scheduling;

/// <summary>
/// Small helpers for registering Quartz jobs with cron/simple triggers pinned to the Lisbon timezone
/// (the legacy scheduler ran Lisbon-local; stats windows and 09:00/18:00 social runs depend on it).
/// Wave-2/3 job registrations call these.
/// </summary>
public static class JobScheduleBuilder
{
    /// <summary>Europe/Lisbon — resolved once. Cron triggers are interpreted in this zone.</summary>
    public static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");

    /// <summary>Register a job with a Lisbon-local cron trigger.</summary>
    public static void AddCronJob<TJob>(this IServiceCollectionQuartzConfigurator quartz, string cron)
        where TJob : IJob
    {
        var key = new JobKey(typeof(TJob).Name);
        quartz.AddJob<TJob>(o => o.WithIdentity(key));
        quartz.AddTrigger(t => t
            .ForJob(key)
            .WithIdentity($"{typeof(TJob).Name}-trigger")
            .WithCronSchedule(cron, x => x.InTimeZone(Lisbon)));
    }

    /// <summary>Register a job that fires on a fixed interval.</summary>
    public static void AddIntervalJob<TJob>(this IServiceCollectionQuartzConfigurator quartz, TimeSpan interval)
        where TJob : IJob
    {
        var key = new JobKey(typeof(TJob).Name);
        quartz.AddJob<TJob>(o => o.WithIdentity(key));
        quartz.AddTrigger(t => t
            .ForJob(key)
            .WithIdentity($"{typeof(TJob).Name}-trigger")
            .WithSimpleSchedule(s => s.WithInterval(interval).RepeatForever()));
    }
}
