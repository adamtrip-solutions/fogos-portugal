using Fogos.Domain.Alerts;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Alerts;

/// <summary>
/// The single write path for <c>alert_events</c>. Inserts are de-duplicated per subscription by the
/// unique (SubscriptionId, DedupeKey) index: a duplicate-key violation means the same alert was already
/// recorded (redelivery / repeated risk run), so the caller must not push again. Returns whether it won.
/// </summary>
public sealed class AlertEventStore(MongoContext mongo, IClock clock)
{
    /// <summary>
    /// Appends an alert event, returning true for the single caller that inserts it (dedupe key unseen)
    /// and false when it already existed — the FCM push should fire only on a true result.
    /// </summary>
    public async Task<bool> TryAppendAsync(
        string subscriptionId, string kind, string? incidentId, string message, string dedupeKey, CancellationToken ct = default)
    {
        var evt = new AlertEvent
        {
            SubscriptionId = subscriptionId,
            Kind = kind,
            IncidentId = incidentId,
            Message = message,
            DedupeKey = dedupeKey,
            CreatedAt = clock.UtcNow,
        };
        try
        {
            await mongo.AlertEvents.InsertOneAsync(evt, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false; // already recorded for this subscription — dedup.
        }
    }
}
