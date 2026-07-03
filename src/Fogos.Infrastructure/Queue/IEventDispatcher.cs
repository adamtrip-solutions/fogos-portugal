using Fogos.Domain.Events;

namespace Fogos.Infrastructure.Queue;

/// <summary>
/// Enqueues a domain event onto a Redis stream (<c>fogos:stream:{stream}</c>) via XADD.
/// Stamps <see cref="IDomainEvent.OccurredAt"/> at enqueue time.
/// </summary>
public interface IEventDispatcher
{
    /// <summary>Dispatch an event to <paramref name="stream"/> (default: the hot <c>default</c> queue).</summary>
    Task DispatchAsync(IDomainEvent evt, string stream = "default", CancellationToken ct = default);
}
