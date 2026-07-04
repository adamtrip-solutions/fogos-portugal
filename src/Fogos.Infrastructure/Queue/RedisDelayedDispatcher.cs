using Fogos.Domain.Events;
using Fogos.Domain.Time;
using StackExchange.Redis;

namespace Fogos.Infrastructure.Queue;

/// <summary>
/// ZADDs an event into <c>fogos:delayed</c> scored by its due-time (unix ms). The Worker's
/// delayed-dispatch pump reclaims due members and re-publishes them onto their target stream.
///
/// Idempotent under at-least-once redelivery: before enqueuing it claims a short-lived dedup key so a
/// redelivered schedule collapses. Handlers re-run on redelivery construct a NEW event (fresh
/// <see cref="IDomainEvent.EventId"/>), so ZADD-member equality can't dedup them — the guard keys on a
/// deterministic identity (kind + target for a push) instead, falling back to the preserved EventId for
/// the same-message-re-dispatched case. The TTL (delay + slack) is the coarse time bucket: two schedules
/// within one debounce window collapse; a genuinely new one after the window schedules again.
/// </summary>
public sealed class RedisDelayedDispatcher(IConnectionMultiplexer redis, IClock clock) : IDelayedDispatcher
{
    private static readonly TimeSpan DedupSlack = TimeSpan.FromMinutes(1);

    public async Task DispatchAsync(IDomainEvent evt, TimeSpan delay, string stream = "default", CancellationToken ct = default)
    {
        evt.OccurredAt = clock.UtcNow;
        var db = redis.GetDatabase();

        var dedupKey = DedupKey(evt);
        var claimed = await db.StringSetAsync(dedupKey, "1", delay + DedupSlack, When.NotExists);
        if (!claimed)
            return; // an equivalent schedule is already pending

        var envelope = new DelayedEnvelope(
            stream,
            EventSerializer.Discriminator(evt),
            EventSerializer.Serialize(evt),
            evt.EventId.ToString("N"));

        var dueAt = clock.UtcNow.Add(delay).ToUnixTimeMilliseconds();
        await db.SortedSetAddAsync(QueueKeys.DelayedSet, envelope.ToJson(), dueAt);
    }

    /// <summary>
    /// Deterministic dedup identity for a delayed schedule. Pushes key on kind+incident (the fields that
    /// make two redelivered pushes "the same"); everything else keys on the preserved EventId.
    /// </summary>
    private static string DedupKey(IDomainEvent evt) => evt switch
    {
        PushNotificationRequested push =>
            $"fogos:delayed:dedup:push:{push.Kind}:{push.IncidentId ?? "-"}",
        _ => $"fogos:delayed:dedup:evt:{evt.EventId:N}",
    };
}
