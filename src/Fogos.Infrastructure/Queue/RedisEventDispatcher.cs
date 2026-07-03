using Fogos.Domain.Events;
using Fogos.Domain.Time;
using StackExchange.Redis;

namespace Fogos.Infrastructure.Queue;

/// <summary>
/// Serializes an event (System.Text.Json, type discriminator) and XADDs it onto its stream.
/// Each stream entry carries three fields: <c>type</c> (discriminator), <c>data</c> (JSON body),
/// and <c>eventId</c> (for correlation in logs and dead-letters).
/// </summary>
public sealed class RedisEventDispatcher(IConnectionMultiplexer redis, IClock clock) : IEventDispatcher
{
    // Field names shared with the consumer — kept here as the single source of truth.
    public const string TypeField = "type";
    public const string DataField = "data";
    public const string EventIdField = "eventId";

    public async Task DispatchAsync(IDomainEvent evt, string stream = "default", CancellationToken ct = default)
    {
        evt.OccurredAt = clock.UtcNow;

        var entry = new NameValueEntry[]
        {
            new(TypeField, EventSerializer.Discriminator(evt)),
            new(DataField, EventSerializer.Serialize(evt)),
            new(EventIdField, evt.EventId.ToString("N")),
        };

        await redis.GetDatabase().StreamAddAsync(QueueKeys.Stream(stream), entry);
    }
}
