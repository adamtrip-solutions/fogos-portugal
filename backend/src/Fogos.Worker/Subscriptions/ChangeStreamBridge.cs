using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Subscriptions;
using HotChocolate.Subscriptions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Fogos.Worker.Subscriptions;

/// <summary>
/// Bridges MongoDB change streams (requires a replica set) to the HotChocolate Redis
/// subscription topics shared with the Api. Watches <c>incidents</c> with full-document
/// lookup, is resilient to invalidation/errors (recreate after a backoff, escalate to ops
/// on repeated failure), and tracks the active set for deltas.
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

    // Resume tokens are kept in memory (per-process). A restarted Worker warms the active set from the
    // DB and starts a fresh stream, so a durable token store buys little here; persisting one would only
    // help bridge a full process restart, which the DB re-warm already covers for correctness.
    private BsonDocument? _incidentResumeToken;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WarmActiveSetAsync(stoppingToken);
        await WatchAsync("incidents", HandleIncidentBatchAsync, stoppingToken);
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

    /// <summary>
    /// After a lost resume token forces a fresh stream, reconcile the in-memory active set against the DB
    /// so events missed in the gap don't leave it drifted, emitting deltas for the net add/remove.
    /// </summary>
    private async Task RewarmActiveSetAsync(CancellationToken ct)
    {
        try
        {
            var ids = (await incidentReads.ActiveAsync([], ct)).Select(i => i.Id);
            var diff = _tracker.Rewarm(ids);
            foreach (var id in diff.Added)
                await PublishDeltaAsync(DeltaKind.Added, id, ct);
            foreach (var id in diff.Removed)
                await PublishDeltaAsync(DeltaKind.Removed, id, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not re-warm active-incident set after a resume-token loss");
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
        using var cursor = await OpenIncidentCursorAsync(ct);
        while (await cursor.MoveNextAsync(ct))
        {
            foreach (var change in cursor.Current)
            {
                _incidentResumeToken = change.ResumeToken; // resume just after this event on recreate

                if (change.OperationType == ChangeStreamOperationType.Invalidate)
                    return; // recreate the stream (StartAfter the invalidate token)

                await HandleIncidentChangeAsync(change, ct);
            }
        }
    }

    private async Task<IChangeStreamCursor<ChangeStreamDocument<Incident>>> OpenIncidentCursorAsync(CancellationToken ct)
    {
        var options = new ChangeStreamOptions
        {
            FullDocument = ChangeStreamFullDocumentOption.UpdateLookup,
            StartAfter = _incidentResumeToken,
        };
        try
        {
            return await context.Incidents.WatchAsync(options, ct);
        }
        catch (Exception ex) when (_incidentResumeToken is not null)
        {
            // The saved token is too old (oplog rolled over / history lost). Drop it, re-warm the active
            // set from the DB so the tracker stays consistent, and start a fresh stream.
            logger.LogWarning(ex, "Incident change-stream resume failed; falling back to a fresh stream and re-warming");
            _incidentResumeToken = null;
            await RewarmActiveSetAsync(ct);
            return await context.Incidents.WatchAsync(
                new ChangeStreamOptions { FullDocument = ChangeStreamFullDocumentOption.UpdateLookup }, ct);
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
