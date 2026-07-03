using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Domain.Warnings;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Subscriptions;
using HotChocolate.Subscriptions;
using MongoDB.Driver;

namespace Fogos.Worker.Subscriptions;

/// <summary>
/// Bridges MongoDB change streams (requires a replica set) to the HotChocolate Redis
/// subscription topics shared with the Api. Watches <c>incidents</c> and <c>warnings</c>
/// with full-document lookup, is resilient to invalidation/errors (recreate after a
/// backoff, escalate to ops on repeated failure), and tracks the active set for deltas.
/// </summary>
public sealed class ChangeStreamBridge(
    MongoContext context,
    IncidentReads incidentReads,
    ITopicEventSender sender,
    IOpsNotifier ops,
    IClock clock,
    ILogger<ChangeStreamBridge> logger) : BackgroundService
{
    private ActiveDeltaTracker _tracker = new([]);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WarmActiveSetAsync(stoppingToken);
        await Task.WhenAll(
            WatchAsync("incidents", HandleIncidentBatchAsync, stoppingToken),
            WatchAsync("warnings", HandleWarningBatchAsync, stoppingToken));
    }

    private async Task WarmActiveSetAsync(CancellationToken ct)
    {
        try
        {
            var ids = (await incidentReads.ActiveAsync([], ct)).Select(i => i.Id);
            _tracker = new ActiveDeltaTracker(ids);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not warm active-incident set; starting empty");
            _tracker = new ActiveDeltaTracker([]);
        }
    }

    private async Task WatchAsync(string name, Func<CancellationToken, Task> watchOnce, CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await watchOnce(ct);
                failures = 0; // the stream ended cleanly (e.g. invalidate) — recreate immediately
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                logger.LogError(ex, "{Stream} change stream failed (attempt {Attempt})", name, failures);
                if (failures == 3)
                    await ops.ErrorAsync($"{name} change stream repeatedly failing: {ex.Message}");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, failures * 5)), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleIncidentBatchAsync(CancellationToken ct)
    {
        var options = new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup };
        using var cursor = await context.Incidents.WatchAsync(options, ct);
        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var change in cursor.Current)
            {
                if (change.OperationType == ChangeStreamOperationType.Invalidate)
                    return; // recreate the stream

                await HandleIncidentChangeAsync(change, ct);
            }
        }
    }

    private async Task HandleIncidentChangeAsync(ChangeStreamDocument<Incident> change, CancellationToken ct)
    {
        switch (change.OperationType)
        {
            case ChangeStreamOperationType.Insert:
            case ChangeStreamOperationType.Update:
            case ChangeStreamOperationType.Replace:
            {
                var incident = change.FullDocument;
                if (incident is null)
                    return; // document deleted between change and lookup

                await PublishIncidentAsync(incident.Id, ct);

                var kind = change.OperationType == ChangeStreamOperationType.Insert
                    ? _tracker.Insert(incident.Id, incident.Active)
                    : _tracker.Update(incident.Id, incident.Active);
                await PublishDeltaAsync(kind, incident.Id, ct);
                break;
            }

            case ChangeStreamOperationType.Delete:
            {
                var id = change.DocumentKey["_id"].AsString;
                await PublishDeltaAsync(_tracker.Delete(id), id, ct);
                break;
            }
        }
    }

    private async Task HandleWarningBatchAsync(CancellationToken ct)
    {
        var options = new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup };
        using var cursor = await context.Warnings.WatchAsync(options, ct);
        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var change in cursor.Current)
            {
                if (change.OperationType == ChangeStreamOperationType.Invalidate)
                    return;

                if (change.OperationType == ChangeStreamOperationType.Insert && change.FullDocument is { } warning)
                    await sender.SendAsync(SubscriptionTopics.WarningAdded, warning.Id, ct);
            }
        }
    }

    private async Task PublishIncidentAsync(string id, CancellationToken ct)
    {
        await sender.SendAsync(SubscriptionTopics.IncidentFirehose, id, ct);
        await sender.SendAsync(SubscriptionTopics.IncidentUpdated(id), id, ct);
    }

    private async Task PublishDeltaAsync(DeltaKind kind, string id, CancellationToken ct)
    {
        if (kind == DeltaKind.None)
            return;

        var message = new ActiveIncidentsDeltaMessage
        {
            At = clock.UtcNow,
            AddedIds = kind == DeltaKind.Added ? [id] : [],
            UpdatedIds = kind == DeltaKind.Updated ? [id] : [],
            RemovedIds = kind == DeltaKind.Removed ? [id] : [],
        };
        await sender.SendAsync(SubscriptionTopics.ActiveIncidentsChanged, message, ct);
    }
}
