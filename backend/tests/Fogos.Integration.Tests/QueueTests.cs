using System.Collections.Concurrent;
using Fogos.Domain.Events;
using Fogos.Infrastructure.DependencyInjection;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Sources;
using Fogos.Worker.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Integration.Tests;

/// <summary>Redis Streams queue: round-trip delivery, retry→dead-letter; FR24 credit meter.</summary>
[Collection("fogos")]
public sealed class QueueTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Dispatch_delivers_event_to_handler()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        var sink = new EventSink();
        await using var sp = BuildProvider(services =>
        {
            services.AddSingleton(sink);
            services.AddScoped<IEventHandler<IncidentCreated>, RecordingIncidentHandler>();
        });

        await using var consumer = StartConsumer(sp, "default");

        await sp.GetRequiredService<IEventDispatcher>().DispatchAsync(new IncidentCreated("inc-1"));

        var delivered = await sink.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(delivered, "handler should have been invoked");
        Assert.Contains("inc-1", sink.Ids);
    }

    [SkippableFact]
    public async Task Failing_handler_dead_letters_after_max_attempts()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        var counter = new FailCounter();
        await using var sp = BuildProvider(
            services =>
            {
                services.AddSingleton(counter);
                services.AddScoped<IEventHandler<IncidentCreated>, AlwaysFailHandler>();
            },
            extraConfig: new Dictionary<string, string?>
            {
                ["Queue:MaxAttempts"] = "3",
                ["Queue:PendingReclaimAfter"] = "00:00:00.400",
            });

        await using var consumer = StartConsumer(sp, "default");

        await sp.GetRequiredService<IEventDispatcher>().DispatchAsync(new IncidentCreated("doomed"));

        var mongo = sp.GetRequiredService<MongoContext>();
        var dead = await PollAsync(
            async () => await mongo.DeadLetters.Find(Builders<BsonDocument>.Filter.Eq("stream", "default")).FirstOrDefaultAsync(),
            d => d is not null,
            TimeSpan.FromSeconds(25));

        Assert.NotNull(dead);
        Assert.Equal("IncidentCreated", dead!["eventType"].AsString);
        Assert.Equal(3, dead["attempts"].AsInt32);
        Assert.Contains("boom", dead["error"].AsString);
        Assert.True(counter.Count >= 3, $"handler should have run 3 times, ran {counter.Count}");
    }

    [SkippableFact]
    public async Task Fr24_credit_meter_trips_at_95_percent()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        await using var sp = BuildProvider(_ => { }, extraConfig: new Dictionary<string, string?>
        {
            ["Sources:Fr24:MonthlyBudget"] = "20", // guard threshold = 20 × 0.95 = 19
        });
        var meter = sp.GetRequiredService<Fr24CreditMeter>();

        for (var i = 0; i < 19; i++)
            Assert.True(await meter.TryConsumeAsync(), $"consume {i} should be allowed");

        Assert.False(await meter.TryConsumeAsync(), "20th consume should trip the 95% guard");
        Assert.Equal(19, await meter.CurrentAsync());
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private ServiceProvider BuildProvider(
        Action<IServiceCollection> configure,
        Dictionary<string, string?>? extraConfig = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = fixture.MongoConnectionString,
            ["Mongo:Database"] = "queue_test_" + Guid.NewGuid().ToString("N")[..8],
            ["Redis:ConnectionString"] = fixture.RedisConnectionString,
        };
        if (extraConfig is not null)
            foreach (var (k, v) in extraConfig)
                settings[k] = v;

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddFogosInfrastructure(config);
        services.AddFogosPipeline(config);
        configure(services);
        return services.BuildServiceProvider();
    }

    private static HostedScope StartConsumer(IServiceProvider sp, string stream) =>
        StartHosted(ActivatorUtilities.CreateInstance<StreamConsumerService>(sp, stream));

    private static HostedScope StartHosted(BackgroundService service)
    {
        service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new HostedScope(service);
    }

    private static async Task<T> PollAsync<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var value = await read();
            if (done(value))
                return value;
            await Task.Delay(200);
        }
        return await read();
    }

    private sealed class HostedScope(BackgroundService service) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try { await service.StopAsync(CancellationToken.None); }
            catch { /* best effort */ }
            service.Dispose();
        }
    }
}

/// <summary>Shared observation point for queue handlers running in DI scopes.</summary>
internal sealed class EventSink
{
    private readonly TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly ConcurrentBag<string> Ids = [];

    public void Add(string id)
    {
        Ids.Add(id);
        _signal.TrySetResult();
    }

    public async Task<bool> WaitAsync(TimeSpan timeout)
    {
        var completed = await Task.WhenAny(_signal.Task, Task.Delay(timeout));
        return completed == _signal.Task;
    }
}

internal sealed class RecordingIncidentHandler(EventSink sink) : IEventHandler<IncidentCreated>
{
    public Task HandleAsync(IncidentCreated evt, CancellationToken ct)
    {
        sink.Add(evt.IncidentId);
        return Task.CompletedTask;
    }
}

internal sealed class FailCounter
{
    private int _count;
    public int Count => _count;
    public void Increment() => Interlocked.Increment(ref _count);
}

internal sealed class AlwaysFailHandler(FailCounter counter) : IEventHandler<IncidentCreated>
{
    public Task HandleAsync(IncidentCreated evt, CancellationToken ct)
    {
        counter.Increment();
        throw new InvalidOperationException("boom");
    }
}
