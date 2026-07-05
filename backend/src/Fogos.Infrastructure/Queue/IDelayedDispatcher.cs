using Fogos.Domain.Events;

namespace Fogos.Infrastructure.Queue;

/// <summary>
/// Enqueues an event for delivery <paramref name="delay"/> from now (a Redis sorted set scored by
/// due-time; a Worker pump moves due entries onto their stream). This is the mechanism behind the
/// legacy FCM 3-minute push debounce.
/// </summary>
public interface IDelayedDispatcher
{
    Task DispatchAsync(IDomainEvent evt, TimeSpan delay, string stream = "default", CancellationToken ct = default);
}
