using Fogos.Domain.Events;

namespace Fogos.Infrastructure.Queue;

/// <summary>
/// Handles one domain-event type. Multiple handlers may register for the same event; the
/// consumer runs each in its own try/catch so one failing handler never blocks its siblings.
/// Handlers must be idempotent (a message can be redelivered) — use <see cref="IProcessedMarker"/>
/// where a side effect must fire at most once.
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent evt, CancellationToken ct);
}
