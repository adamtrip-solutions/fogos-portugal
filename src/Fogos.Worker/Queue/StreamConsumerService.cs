using Fogos.Domain.Events;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Queue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using StackExchange.Redis;

namespace Fogos.Worker.Queue;

/// <summary>
/// Consumes one Redis stream via the <c>workers</c> consumer group. New messages arrive through
/// XREADGROUP; stale pending (delivered-but-unacked) messages are reclaimed via XPENDING/XCLAIM and
/// retried. Each event resolves its <c>IEventHandler&lt;TEvent&gt;</c> registrations and runs each in
/// isolation; a message that exhausts <see cref="QueueOptions.MaxAttempts"/> is dead-lettered to
/// Mongo with an ops error. Acks only on success (or terminal dead-letter).
/// </summary>
public sealed class StreamConsumerService(
    string stream,
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    MongoContext mongo,
    IOpsNotifier ops,
    IClock clock,
    IOptions<QueueOptions> options,
    ILogger<StreamConsumerService> logger) : BackgroundService
{
    private readonly string _consumerName = $"{Environment.MachineName}-{stream}-{Guid.NewGuid():N}"[..48];
    private string StreamKey => QueueKeys.Stream(stream);
    private string Group => options.Value.ConsumerGroup;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureGroupAsync();
        var db = redis.GetDatabase();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReclaimStaleAsync(db, stoppingToken);

                var entries = await db.StreamReadGroupAsync(StreamKey, Group, _consumerName, ">", count: 16);
                if (entries.Length == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                    continue;
                }

                foreach (var entry in entries)
                    await ProcessAsync(db, entry, attempt: 1, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stream consumer loop error on {Stream}", stream);
                try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task EnsureGroupAsync()
    {
        var db = redis.GetDatabase();
        try
        {
            await db.StreamCreateConsumerGroupAsync(StreamKey, Group, StreamPosition.Beginning, createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Group already exists — fine.
        }
    }

    private async Task ReclaimStaleAsync(IDatabase db, CancellationToken ct)
    {
        var minIdle = (long)options.Value.PendingReclaimAfter.TotalMilliseconds;
        var pending = await db.StreamPendingMessagesAsync(StreamKey, Group, count: 32, consumerName: RedisValue.Null);

        foreach (var p in pending)
        {
            if (ct.IsCancellationRequested)
                break;
            if (p.IdleTimeInMilliseconds < minIdle)
                continue;

            // The delivery count already reached the ceiling — dead-letter without reprocessing.
            if (p.DeliveryCount >= options.Value.MaxAttempts)
            {
                await DeadLetterByIdAsync(db, p.MessageId, (int)p.DeliveryCount, "max attempts exceeded", ct);
                continue;
            }

            var claimed = await db.StreamClaimAsync(StreamKey, Group, _consumerName, minIdle, [p.MessageId]);
            if (claimed.Length == 0)
                continue; // already acked/removed by someone else

            await ProcessAsync(db, claimed[0], attempt: (int)p.DeliveryCount + 1, ct);
        }
    }

    private async Task ProcessAsync(IDatabase db, StreamEntry entry, int attempt, CancellationToken ct)
    {
        var type = entry[RedisEventDispatcher.TypeField];
        var data = entry[RedisEventDispatcher.DataField];
        if (!type.HasValue || !data.HasValue)
        {
            await DeadLetterAsync(db, entry.Id, "?", data.HasValue ? data! : "", "malformed entry (missing fields)", attempt, ct);
            return;
        }

        var clrType = EventSerializer.Resolve(type!);
        if (clrType is null)
        {
            await DeadLetterAsync(db, entry.Id, type!, data!, "unknown event type", attempt, ct);
            return;
        }

        IDomainEvent evt;
        try
        {
            evt = EventSerializer.Deserialize(clrType, data!);
        }
        catch (Exception ex)
        {
            await DeadLetterAsync(db, entry.Id, type!, data!, $"deserialization failed: {ex.Message}", attempt, ct);
            return;
        }

        var errors = await DispatchToHandlersAsync(clrType, evt, ct);
        if (errors.Count == 0)
        {
            await db.StreamAcknowledgeAsync(StreamKey, Group, entry.Id);
            return;
        }

        var aggregate = string.Join("; ", errors);
        if (attempt >= options.Value.MaxAttempts)
        {
            await DeadLetterAsync(db, entry.Id, type!, data!, aggregate, attempt, ct);
            return;
        }

        // Leave the message pending; the reclaim path retries it (incrementing its delivery count).
        logger.LogWarning("Handler failure on {Stream} attempt {Attempt}: {Error}", stream, attempt, aggregate);
    }

    /// <summary>Runs every registered handler for the event's concrete type, each isolated. Returns errors.</summary>
    private async Task<List<string>> DispatchToHandlersAsync(Type eventType, IDomainEvent evt, CancellationToken ct)
    {
        var errors = new List<string>();
        var handlerType = typeof(Fogos.Infrastructure.Queue.IEventHandler<>).MakeGenericType(eventType);
        var method = handlerType.GetMethod("HandleAsync")!;

        using var scope = scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            if (handler is null)
                continue;
            try
            {
                await (Task)method.Invoke(handler, [evt, ct])!;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                logger.LogError(inner, "Handler {Handler} failed for {EventType}", handler.GetType().Name, eventType.Name);
                errors.Add($"{handler.GetType().Name}: {inner.Message}");
            }
        }

        return errors;
    }

    private async Task DeadLetterByIdAsync(IDatabase db, RedisValue messageId, int attempts, string error, CancellationToken ct)
    {
        var range = await db.StreamRangeAsync(StreamKey, messageId, messageId, count: 1);
        var entry = range.Length > 0 ? range[0] : default;
        var type = entry.Values is not null ? entry[RedisEventDispatcher.TypeField] : RedisValue.Null;
        var data = entry.Values is not null ? entry[RedisEventDispatcher.DataField] : RedisValue.Null;
        await DeadLetterAsync(db, messageId, type.HasValue ? type! : "?", data.HasValue ? data! : "", error, attempts, ct);
    }

    private async Task DeadLetterAsync(IDatabase db, RedisValue messageId, string eventType, string payload, string error, int attempts, CancellationToken ct)
    {
        var doc = new BsonDocument
        {
            ["stream"] = stream,
            ["eventType"] = eventType,
            ["payload"] = payload,
            ["error"] = error,
            ["attempts"] = attempts,
            ["diedAt"] = clock.UtcNow.UtcDateTime,
        };

        try
        {
            await mongo.DeadLetters.InsertOneAsync(doc, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write dead-letter for {EventType}", eventType);
        }

        await db.StreamAcknowledgeAsync(StreamKey, Group, messageId);
        await ops.ErrorAsync($"☠️ dead-letter [{stream}/{eventType}] after {attempts} attempts: {error}", ct);
    }
}
