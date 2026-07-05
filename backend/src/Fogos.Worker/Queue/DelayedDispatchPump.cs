using Fogos.Domain.Time;
using Fogos.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Fogos.Worker.Queue;

/// <summary>
/// Moves due entries from the <c>fogos:delayed</c> sorted set onto their target stream. The pop is
/// atomic (a Lua ZRANGEBYSCORE+ZREM) so competing pumps never double-deliver. This is the delivery
/// half of the delayed-dispatch mechanism behind the FCM 3-minute push debounce.
/// </summary>
public sealed class DelayedDispatchPump(
    IConnectionMultiplexer redis,
    IClock clock,
    ILogger<DelayedDispatchPump> logger) : BackgroundService
{
    // Atomically claim up to ARGV[2] members whose score (due-time) is <= ARGV[1] (now).
    private const string PopDueScript = """
        local due = redis.call('ZRANGEBYSCORE', KEYS[1], '-inf', ARGV[1], 'LIMIT', 0, ARGV[2])
        for i = 1, #due do
            redis.call('ZREM', KEYS[1], due[i])
        end
        return due
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = redis.GetDatabase();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PumpOnceAsync(db);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Delayed dispatch pump error");
            }

            try { await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PumpOnceAsync(IDatabase db)
    {
        var now = clock.UtcNow.ToUnixTimeMilliseconds();
        var result = await db.ScriptEvaluateAsync(
            PopDueScript,
            [QueueKeys.DelayedSet],
            [now, 100]);

        if (result.IsNull)
            return;

        foreach (var member in (RedisResult[])result!)
        {
            var json = (string?)member;
            if (string.IsNullOrEmpty(json))
                continue;

            DelayedEnvelope envelope;
            try
            {
                envelope = DelayedEnvelope.FromJson(json);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dropping malformed delayed envelope");
                continue;
            }

            var entry = new NameValueEntry[]
            {
                new(RedisEventDispatcher.TypeField, envelope.Type),
                new(RedisEventDispatcher.DataField, envelope.Data),
                new(RedisEventDispatcher.EventIdField, envelope.EventId),
            };
            await db.StreamAddAsync(QueueKeys.Stream(envelope.Stream), entry);
        }
    }
}
