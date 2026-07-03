using Fogos.Api.GraphQL.DataLoaders;
using Fogos.Api.GraphQL.Types;
using Fogos.Domain.Incidents;
using Fogos.Domain.Warnings;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Subscriptions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;

namespace Fogos.Api.GraphQL.Subscriptions;

/// <summary>
/// Live schema root. Messages carry only ids over Redis; full entities are re-hydrated
/// here through DataLoaders, so the same objects/resolvers serve queries and subscriptions.
/// </summary>
public sealed class Subscription
{
    // ── incidentUpdated(id) ────────────────────────────────────────────────────
    public ValueTask<ISourceStream<string>> SubscribeToIncidentUpdatedAsync(
        [Service] ITopicEventReceiver receiver,
        CancellationToken ct,
        [ID] string? id = null) =>
        id is null
            ? receiver.SubscribeAsync<string>(SubscriptionTopics.IncidentFirehose, ct)
            : receiver.SubscribeAsync<string>(SubscriptionTopics.IncidentUpdated(id), ct);

    [Subscribe(With = nameof(SubscribeToIncidentUpdatedAsync))]
    public async Task<Incident> IncidentUpdated(
        [EventMessage] string incidentId,
        IncidentByIdDataLoader loader,
        CancellationToken ct) =>
        (await loader.LoadAsync(incidentId, ct))!;

    // ── activeIncidentsChanged ─────────────────────────────────────────────────
    public ValueTask<ISourceStream<ActiveIncidentsDeltaMessage>> SubscribeToActiveIncidentsAsync(
        [Service] ITopicEventReceiver receiver,
        CancellationToken ct) =>
        receiver.SubscribeAsync<ActiveIncidentsDeltaMessage>(SubscriptionTopics.ActiveIncidentsChanged, ct);

    [Subscribe(With = nameof(SubscribeToActiveIncidentsAsync))]
    public async Task<ActiveIncidentsDelta> ActiveIncidentsChanged(
        [EventMessage] ActiveIncidentsDeltaMessage message,
        IncidentByIdDataLoader loader,
        CancellationToken ct)
    {
        var added = await LoadManyAsync(loader, message.AddedIds, ct);
        var updated = await LoadManyAsync(loader, message.UpdatedIds, ct);
        return new ActiveIncidentsDelta(message.At, added, updated, message.RemovedIds);
    }

    // ── warningAdded ───────────────────────────────────────────────────────────
    public ValueTask<ISourceStream<string>> SubscribeToWarningAddedAsync(
        [Service] ITopicEventReceiver receiver,
        CancellationToken ct) =>
        receiver.SubscribeAsync<string>(SubscriptionTopics.WarningAdded, ct);

    [Subscribe(With = nameof(SubscribeToWarningAddedAsync))]
    public async Task<Warning> WarningAdded(
        [EventMessage] string warningId,
        WarningReads reads,
        CancellationToken ct) =>
        (await reads.GetByIdAsync(warningId, ct))!;

    private static async Task<IReadOnlyList<Incident>> LoadManyAsync(
        IncidentByIdDataLoader loader,
        IReadOnlyList<string> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];
        var loaded = await loader.LoadAsync(ids, ct);
        return loaded.Where(i => i is not null).Select(i => i!).ToList();
    }
}
