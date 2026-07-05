using Fogos.Infrastructure.Scheduling;
using Quartz;
using Quartz.Impl;

namespace Fogos.Integration.Tests.Firms;

/// <summary>HTTP stub that answers based on the request URL (FIRMS source discriminates VIIRS vs MODIS).</summary>
internal sealed class UrlStubHandler(Func<string, HttpResponseMessage> responder) : HttpMessageHandler
{
    private int _calls;
    public int Calls => _calls;

    /// <summary>Every requested URL, in order.</summary>
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        var url = request.RequestUri!.ToString();
        lock (Requests)
            Requests.Add(url);
        return Task.FromResult(responder(url));
    }
}

/// <summary>Single-flight lock double that always grants (no Redis needed for the job branch under test).</summary>
internal sealed class AlwaysGrantLock : ISingleFlightLock
{
    public Task<string?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult<string?>("token");

    public Task ReleaseAsync(string key, string token, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Minimal <see cref="IJobExecutionContext"/> exposing only the job key + cancellation token used by the jobs.</summary>
internal sealed class FakeJobContext(string jobName, CancellationToken ct = default) : IJobExecutionContext
{
    public IJobDetail JobDetail { get; } = JobBuilder.Create<ProcessFirmsJobMarker>().WithIdentity(jobName).Build();
    public CancellationToken CancellationToken { get; } = ct;
    public JobDataMap MergedJobDataMap { get; } = new();

    public IScheduler Scheduler => throw new NotSupportedException();
    public ITrigger Trigger => throw new NotSupportedException();
    public ICalendar? Calendar => null;
    public bool Recovering => false;
    public TriggerKey RecoveringTriggerKey => throw new InvalidOperationException("Not recovering.");
    public int RefireCount => 0;
    public IJob JobInstance => throw new NotSupportedException();
    public DateTimeOffset FireTimeUtc => DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFireTimeUtc => null;
    public DateTimeOffset? PreviousFireTimeUtc => null;
    public DateTimeOffset? NextFireTimeUtc => null;
    public string FireInstanceId => Guid.NewGuid().ToString("N");
    public object? Result { get; set; }
    public TimeSpan JobRunTime => TimeSpan.Zero;

    public void Put(object key, object objectValue) { }
    public object? Get(object key) => null;
}

/// <summary>Placeholder job type so <see cref="FakeJobContext"/> can build a real <see cref="IJobDetail"/>.</summary>
internal sealed class ProcessFirmsJobMarker : IJob
{
    public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
}
