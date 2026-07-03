using Fogos.Domain.Events;
using Fogos.Domain.Time;
using StackExchange.Redis;

namespace Fogos.Infrastructure.Queue;

/// <summary>
/// ZADDs an event into <c>fogos:delayed</c> scored by its due-time (unix ms). The Worker's
/// delayed-dispatch pump reclaims due members and re-publishes them onto their target stream.
/// </summary>
public sealed class RedisDelayedDispatcher(IConnectionMultiplexer redis, IClock clock) : IDelayedDispatcher
{
    public async Task DispatchAsync(IDomainEvent evt, TimeSpan delay, string stream = "default", CancellationToken ct = default)
    {
        evt.OccurredAt = clock.UtcNow;

        var envelope = new DelayedEnvelope(
            stream,
            EventSerializer.Discriminator(evt),
            EventSerializer.Serialize(evt),
            evt.EventId.ToString("N"));

        var dueAt = clock.UtcNow.Add(delay).ToUnixTimeMilliseconds();
        await redis.GetDatabase().SortedSetAddAsync(QueueKeys.DelayedSet, envelope.ToJson(), dueAt);
    }
}
