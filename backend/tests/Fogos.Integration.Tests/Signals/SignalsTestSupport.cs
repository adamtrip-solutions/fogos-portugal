using Fogos.Domain.Events;
using Fogos.Infrastructure.Queue;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Signals;

/// <summary>Helpers shared by the signals integration tests: reading dispatched events off a stream.</summary>
internal static class SignalsTestSupport
{
    /// <summary>Deserializes every resolvable event currently on <paramref name="stream"/> (default queue).</summary>
    public static async Task<IReadOnlyList<IDomainEvent>> ReadEventsAsync(
        IConnectionMultiplexer redis, string stream = "default")
    {
        var entries = await redis.GetDatabase().StreamRangeAsync(QueueKeys.Stream(stream));
        var events = new List<IDomainEvent>();
        foreach (var entry in entries)
        {
            var type = entry[RedisEventDispatcher.TypeField];
            var data = entry[RedisEventDispatcher.DataField];
            var clr = EventSerializer.Resolve(type!);
            if (clr is null)
                continue;
            events.Add(EventSerializer.Deserialize(clr, data!));
        }
        return events;
    }
}
