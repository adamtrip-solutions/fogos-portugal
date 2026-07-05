using System.Collections.Concurrent;
using Fogos.Domain.Events;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Queue;
using Quartz;

namespace Fogos.Integration.Tests.Planes;

/// <summary>A settable clock so the daylight gate and the 30-minute dedup window are deterministic.</summary>
internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public DateTimeOffset LisbonNow => TimeZoneInfo.ConvertTime(UtcNow, FogosClock.Lisbon);

    public DateOnly LisbonToday => DateOnly.FromDateTime(LisbonNow.Date);

    public DateTimeOffset FromLisbon(DateTime naiveLocal)
    {
        var unspecified = DateTime.SpecifyKind(naiveLocal, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, FogosClock.Lisbon.GetUtcOffset(unspecified));
    }

    public DateTimeOffset ToLisbon(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, FogosClock.Lisbon);
}

/// <summary>Captures scheduled push events instead of enqueueing them on Redis.</summary>
internal sealed class RecordingDelayedDispatcher : IDelayedDispatcher
{
    public readonly ConcurrentBag<IDomainEvent> Dispatched = [];

    public Task DispatchAsync(IDomainEvent evt, TimeSpan delay, string stream = "default", CancellationToken ct = default)
    {
        Dispatched.Add(evt);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Minimal <see cref="IJobExecutionContext"/> for invoking a job's <c>Execute</c> directly in a test.
/// The plane jobs only read <see cref="CancellationToken"/>; every other member is unused.
/// </summary>
internal sealed class FakeJobExecutionContext(CancellationToken cancellationToken) : IJobExecutionContext
{
    public CancellationToken CancellationToken { get; } = cancellationToken;

    public IScheduler Scheduler => throw new NotSupportedException();
    public ITrigger Trigger => throw new NotSupportedException();
    public ICalendar? Calendar => throw new NotSupportedException();
    public bool Recovering => throw new NotSupportedException();
    public TriggerKey RecoveringTriggerKey => throw new NotSupportedException();
    public int RefireCount => throw new NotSupportedException();
    public JobDataMap MergedJobDataMap => throw new NotSupportedException();
    public IJobDetail JobDetail => throw new NotSupportedException();
    public IJob JobInstance => throw new NotSupportedException();
    public DateTimeOffset FireTimeUtc => throw new NotSupportedException();
    public DateTimeOffset? ScheduledFireTimeUtc => throw new NotSupportedException();
    public DateTimeOffset? PreviousFireTimeUtc => throw new NotSupportedException();
    public DateTimeOffset? NextFireTimeUtc => throw new NotSupportedException();
    public string FireInstanceId => throw new NotSupportedException();
    public object? Result { get; set; }
    public TimeSpan JobRunTime => throw new NotSupportedException();

    public object? Get(object key) => throw new NotSupportedException();
    public void Put(object key, object objectValue) => throw new NotSupportedException();
}
