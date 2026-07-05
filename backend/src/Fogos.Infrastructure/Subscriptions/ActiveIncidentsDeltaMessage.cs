namespace Fogos.Infrastructure.Subscriptions;

/// <summary>
/// Lightweight active-set delta pushed over Redis. Only ids cross the wire — the Api
/// re-hydrates full <c>Incident</c> objects through DataLoaders, so the payload stays
/// small and there is no cross-process serialization of the domain entity.
/// </summary>
public sealed record ActiveIncidentsDeltaMessage
{
    public DateTimeOffset At { get; init; }
    public IReadOnlyList<string> AddedIds { get; init; } = [];
    public IReadOnlyList<string> UpdatedIds { get; init; } = [];
    public IReadOnlyList<string> RemovedIds { get; init; } = [];
}
